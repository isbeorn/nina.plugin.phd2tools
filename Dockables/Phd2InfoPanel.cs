using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Core.Locale;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Equipment.MyGuider.PHD2.PhdEvents;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Phd2Tools.Dockables {

    [Export(typeof(IDockableVM))]
    public partial class Phd2InfoPanel : DockableVM {
        private IGuiderMediator guiderMediator;
        private readonly IImageDataFactory imageDataFactory;

        [ImportingConstructor]
        public Phd2InfoPanel(IProfileService profileService, IGuiderMediator guiderMediator, IImageDataFactory imageDataFactory) : base(profileService) {
            Title = "PHD2 Info";
            //var dict = new ResourceDictionary();
            //dict.Source = new Uri("NINA.Plugin.Phd2Tools;component/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            //ImageGeometry = (System.Windows.Media.GeometryGroup)dict["ScopeControlSVG"];
            //ImageGeometry.Freeze();

            this.guiderMediator = guiderMediator;
            this.imageDataFactory = imageDataFactory;
            this.guiderMediator.GuideEvent += GuiderMediator_GuideEvent;

            _ = Task.Run(Refresh);
        }

        private TcpClient client;
        private Stream stream;
        private StreamReader reader;
        private readonly SemaphoreSlim writeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> pending = new();
        private CancellationTokenSource receiveCts = new();
        private Task receiveTask;

        private void GuiderMediator_GuideEvent(object sender, Core.Interfaces.IGuideStep e) {
            if (IsVisible && guiderMediator.GetInfo().Connected) {
                if (guiderMediator.GetDevice() is PHD2Guider phd2Guider) {
                    if (e is PhdEventGuideStep eventGuideStep) {
                        HFD = eventGuideStep.HFD;
                        StarMass = eventGuideStep.StarMass;
                        SNR = eventGuideStep.SNR;
                    }
                }
            }
        }

        private CancellationTokenSource refreshTokenSource;

        [ObservableProperty]
        private BitmapSource starImage;

        [ObservableProperty]
        private string appState;

        [ObservableProperty]
        private double exposureTime;

        [ObservableProperty]
        private double fWHM;

        [ObservableProperty]
        private List<DataPoint> midrowPoints;

        [ObservableProperty]
        private double hFD;

        [ObservableProperty]
        private double starMass;

        [ObservableProperty]
        private double sNR;

        [ObservableProperty]
        private ushort peak;

        [ObservableProperty]
        private DataPoint starCenter;

        private async Task Refresh() {
            try {
                using (refreshTokenSource = new CancellationTokenSource()) {
                    var ct = refreshTokenSource.Token;
                    while (!ct.IsCancellationRequested) {
                        var interval = 2000;
                        var start = DateTime.UtcNow;
                        try {
                            if (IsVisible && guiderMediator.GetInfo().Connected) {
                                if (guiderMediator.GetDevice() is PHD2Guider phd2Guider) {
                                    if (client?.Connected != true) {
                                        var type = phd2Guider.GetType();
                                        var phd2Ip = type.GetField("phd2Ip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(phd2Guider) as IPAddress;

                                        await EnsureConnectedAsync(phd2Ip, profileService.ActiveProfile.GuiderSettings.PHD2ServerPort);
                                    }

                                    var exposureDurationResponse = await SendMessage<GetExposureResponse>(new Phd2GetExposure());
                                    interval = Math.Max(exposureDurationResponse.result, 2000);

                                    ExposureTime = TimeSpan.FromMilliseconds(exposureDurationResponse.result).TotalSeconds;

                                    AppState = await GetAppState();

                                    if (AppState == PhdAppState.SELECTED || AppState == PhdAppState.CALIBRATING || AppState == PhdAppState.GUIDING || AppState == PhdAppState.LOSTLOCK) {
                                        await GetPhd2Image();
                                    } else {
                                        StarImage = null;
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Logger.Error(ex);
                        }

                        var remaining = TimeSpan.FromMilliseconds(interval) - (DateTime.UtcNow - start);
                        if (remaining > TimeSpan.Zero) {
                            await Task.Delay(remaining, ct);
                        }
                    }
                }
            } catch { }
        }

        private async Task ReceiveLoopAsync(CancellationToken token) {
            try {
                while (!token.IsCancellationRequested) {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null) break; // disconnected

                    JObject o;
                    try { o = JObject.Parse(line); } catch (Exception ex) {
                        Logger.Warning($"Phd2 - Invalid JSON: {ex.Message}");
                        continue;
                    }

                    var id = (string?)o["id"];
                    if (!string.IsNullOrEmpty(id) && pending.TryRemove(id, out var tcs)) {
                        tcs.TrySetResult(o);
                    } else {
                        // Unsolicited event/notification (no id or no waiter) — route to an event handler if you have one
                        Logger.Debug($"Phd2 - Unsolicited: {line}");
                    }
                }
            } catch (Exception ex) {
                Logger.Error("Phd2 receive loop error", ex);
            } finally {
                // fault all waiters on connection loss
                foreach (var kv in pending)
                    kv.Value.TrySetException(new IOException("Connection closed"));
                pending.Clear();
            }
        }

        public async Task<T> SendMessage<T>(Phd2Method msg, int receiveTimeout = 60000) where T : PhdMethodResponse, new() {
            var id = msg.Id;
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(id, tcs))
                throw new InvalidOperationException($"Duplicate message id '{id}' in flight.");

            try {
                // Serialize once, newline-delimited
                var serialized = JsonConvert.SerializeObject(
                    msg,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                Logger.Debug($"Phd2 - Sending '{serialized}'");

                var payload = Encoding.UTF8.GetBytes(serialized + "\n");

                await writeLock.WaitAsync().ConfigureAwait(false);
                try {
                    await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                } finally {
                    writeLock.Release();
                }

                using var cts = new CancellationTokenSource(receiveTimeout);
                using var reg = cts.Token.Register(() => {
                    if (pending.TryRemove(id, out var waiter))
                        waiter.TrySetException(new TimeoutException($"Timed out waiting for PHD2 reply for id '{id}'."));
                });
                var obj = await tcs.Task.ConfigureAwait(false);

                // Deserialize to T, check errors
                var response = obj.ToObject<T>() ?? new T { id = id, error = new PhdError { code = -1, message = "Null response" } };
                CheckPhdError(response);
                Logger.Debug($"Phd2 - Received answer '{obj.ToString(Formatting.None)}'");
                return response;
            } catch (Exception ex) {
                Logger.Error("Phd2 error while sending message", ex);
                return new T { id = id, error = new PhdError { code = -1, message = "Unable to get response from PHD2" } };
            } finally {
                // If the request completed normally, ReceiveLoop removed it; if we hit an early exception before removal, ensure cleanup.
                pending.TryRemove(id, out _);
            }
        }

        private async Task EnsureConnectedAsync(IPAddress ip, int port) {
            if (client is { Connected: true } && stream?.CanWrite == true && stream?.CanRead == true)
                return;

            // Tear down old stuff first
            try { receiveCts?.Cancel(); } catch { }
            try { await (receiveTask ?? Task.CompletedTask); } catch { }
            reader?.Dispose();
            stream?.Dispose();
            client?.Dispose();

            client = new TcpClient(AddressFamily.InterNetwork) {
                NoDelay = true
            };
            await client.ConnectAsync(ip, port).ConfigureAwait(false);
            stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            receiveCts = new CancellationTokenSource();
            receiveTask = Task.Run(() => ReceiveLoopAsync(receiveCts.Token));
        }

        private static void CheckPhdError(PhdMethodResponse m) {
            if (m.error != null) {
                Notification.ShowError(String.Format(Loc.Instance["LblPHDError"], m.error.message, m.error.code));
                Logger.Warning("PHDError: " + m.error.message + " CODE: " + m.error.code);
            }
        }

        private async Task GetPhd2Image() {
            try {
                PhdImageResultResponse res = await SendMessage<PhdImageResultResponse>(new Phd2GetStarImage());
                if (res.error == null && res.result != null && res.result.pixels != null) {
                    byte[] raw = Convert.FromBase64String(res.result.pixels.Trim('\0'));
                    ushort[] pixels = new ushort[raw.Length / 2];
                    Buffer.BlockCopy(raw, 0, pixels, 0, raw.Length);

                    var midrowdata = GetMidrow(pixels, res.result.width, res.result.height);
                    Peak = midrowdata.Max();
                    MidrowPoints = Enumerable.Range(0, midrowdata.Length).Select(x => new DataPoint(x, midrowdata[x])).ToList();
                    FWHM = CalculateFWHM(midrowdata);
                    StarCenter = new DataPoint(res.result.star_pos[0], res.result.star_pos[1]);

                    var iarr = new ImageArray(pixels);

                    var baseData = imageDataFactory.CreateBaseImageData(iarr, res.result.width, res.result.height, 16, false, new ImageMetaData());
                    var render = baseData.RenderImage();
                    var bmpSource = await ImageUtility.Stretch(render, 0.25, -2.8);

                    StarImage = bmpSource;
                } else {
                    StarImage = null;
                }
            } catch (Exception) {
            }
        }

        private async Task<string> GetAppState() {
            var msg = new Phd2GetAppState();
            var appStateResponse = await SendMessage<GenericPhdMethodResponse>(msg);
            return appStateResponse?.result?.ToString();
        }

        public void Dispose() {
            try { refreshTokenSource?.Cancel(); } catch { }
            try { receiveCts?.Cancel(); } catch { }
            try { receiveTask?.Wait(2000); } catch { }

            reader?.Dispose();
            stream?.Dispose();
            client?.Dispose();
            writeLock?.Dispose();
            receiveCts?.Dispose();

            guiderMediator.GuideEvent -= GuiderMediator_GuideEvent;
        }

        private ushort[] GetMidrow(ushort[] pixels, int width, int height) {
            ushort[] midrowdata = new ushort[width];
            var halfrow = height / 2;

            ushort maxValue = 0;
            ushort minValue = ushort.MaxValue;

            for (int i = 0; i < width; i++) {
                var pixel = pixels[halfrow * width + i];
                midrowdata[i] = pixel;

                if (pixel > maxValue) {
                    maxValue = pixel;
                }
                if (pixel < minValue) {
                    minValue = pixel;
                }
            }
            return midrowdata;
        }

        private double CalculateFWHM(ushort[] midrowdata) {
            var minValue = midrowdata.Min();
            var maxValue = midrowdata.Max();

            var halfMax = (maxValue - minValue) / 2d + minValue;

            int x1 = 0;
            int x2 = 0;
            int profval;
            int profvalprec;

            for (int i = 1; i < midrowdata.Length; i++) {
                profval = midrowdata[i];
                profvalprec = midrowdata[i - 1];
                if (profvalprec <= halfMax && profval >= halfMax) {
                    x1 = i;
                } else if (profvalprec >= halfMax && profval <= halfMax) {
                    x2 = i;
                }
            }

            profval = midrowdata[x1];
            profvalprec = midrowdata[x1 - 1];
            float f1 = (float)x1 - (float)(profval - halfMax) / (float)(profval - profvalprec);
            profval = midrowdata[x2];
            profvalprec = midrowdata[x2 - 1];
            float f2 = (float)x2 - (float)(profvalprec - halfMax) / (float)(profvalprec - profval);
            return f2 - f1;
        }
    }

    public class PhdImageResultResponse : PhdMethodResponse {
        public PhdImageResult result { get; set; }
    }

    public class PhdImageResult {
        public int frame;

        public int width;

        public int height;

        public double[] star_pos;

        public string pixels;
    }
}