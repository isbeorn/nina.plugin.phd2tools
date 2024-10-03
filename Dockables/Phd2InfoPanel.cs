using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
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

        private void GuiderMediator_GuideEvent(object sender, Core.Interfaces.IGuideStep e) {
            if (IsVisible && guiderMediator.GetInfo().Connected) {
                if (guiderMediator.GetDevice() is PHD2Guider phd2Guider) {
                    if (e is PhdEventGuideStep eventGuideStep) {
                        HFD = eventGuideStep.StarMass;
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
                                    var exposureDurationResponse = await phd2Guider.SendMessage<GetExposureResponse>(new Phd2GetExposure());
                                    interval = Math.Max(exposureDurationResponse.result, 2000);

                                    ExposureTime = TimeSpan.FromMilliseconds(exposureDurationResponse.result).TotalSeconds;

                                    AppState = await GetAppState(phd2Guider);

                                    if (AppState == PhdAppState.SELECTED || AppState == PhdAppState.CALIBRATING || AppState == PhdAppState.GUIDING || AppState == PhdAppState.LOSTLOCK) {
                                        await GetPhd2Image(phd2Guider);
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

        private async Task GetPhd2Image(PHD2Guider phd2Guider) {
            try {
                PhdImageResultResponse res = await phd2Guider.SendMessage<PhdImageResultResponse>(new Phd2GetStarImage());
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

        private async Task<string> GetAppState(PHD2Guider phd2Guider) {
            var msg = new Phd2GetAppState();
            var appStateResponse = await phd2Guider.SendMessage(msg);
            return appStateResponse?.result?.ToString();
        }

        public void Dispose() {
            try {
                refreshTokenSource?.Cancel();
            } catch { }
            this.guiderMediator.GuideEvent -= GuiderMediator_GuideEvent;
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