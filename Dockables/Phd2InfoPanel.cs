using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
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

        [ImportingConstructor]
        public Phd2InfoPanel(IProfileService profileService, IGuiderMediator guiderMediator) : base(profileService) {
            Title = "PHD2 Info";
            //var dict = new ResourceDictionary();
            //dict.Source = new Uri("NINA.Plugin.Phd2Tools;component/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            //ImageGeometry = (System.Windows.Media.GeometryGroup)dict["ScopeControlSVG"];
            //ImageGeometry.Freeze();

            this.guiderMediator = guiderMediator;

            _ = Task.Run(() => Refresh(new CancellationToken()));
        }

        [ObservableProperty]
        private BitmapSource starImage;

        [ObservableProperty]
        private string appState;

        [ObservableProperty]
        private double exposureTime;

        private async Task Refresh(CancellationToken ct) {
            try {
                while (!ct.IsCancellationRequested) {
                    var interval = 1000;
                    var start = DateTime.Now;
                    try {
                        if (IsVisible && guiderMediator.GetInfo().Connected) {
                            if (guiderMediator.GetDevice() is PHD2Guider phd2Guider) {
                                var exposureDurationResponse = await phd2Guider.SendMessage<GetExposureResponse>(new Phd2GetExposure());
                                interval = exposureDurationResponse.result;

                                ExposureTime = TimeSpan.FromMilliseconds(interval).TotalSeconds;

                                AppState = await GetAppState(phd2Guider);

                                if (AppState == PhdAppState.SELECTED || AppState == PhdAppState.GUIDING || AppState == PhdAppState.LOSTLOCK || AppState == PhdAppState.CALIBRATING) {
                                    StarImage = await GetPhd2Image(phd2Guider);
                                } else {
                                    StarImage = null;
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Logger.Error(ex);
                    }

                    var remaining = TimeSpan.FromMilliseconds(interval) - (DateTime.Now - start);
                    if (remaining > TimeSpan.Zero) {
                        await Task.Delay(remaining, ct);
                    }
                }
            } catch { }
        }

        private async Task<BitmapSource> GetPhd2Image(PHD2Guider phd2Guider) {
            PhdImageResultResponse res = await phd2Guider.SendMessage<PhdImageResultResponse>(new Phd2GetStarImage());
            if (res.error == null && res.result != null && res.result.pixels != null) {
                byte[] raw = Convert.FromBase64String(res.result.pixels.Trim('\0'));
                ushort[] pixels = new ushort[raw.Length / 2];
                Buffer.BlockCopy(raw, 0, pixels, 0, raw.Length);

                var iarr = new ImageArray(pixels);
                var bmpSource = ImageUtility.CreateSourceFromArray(iarr, new ImageProperties(res.result.width, res.result.height, 16, false, 0), PixelFormats.Gray16);
                bmpSource.Freeze();
                return bmpSource;
            }
            return null;
        }

        private async Task<string> GetAppState(PHD2Guider phd2Guider) {
            var msg = new Phd2GetAppState();
            var appStateResponse = await phd2Guider.SendMessage(msg);
            return appStateResponse?.result?.ToString();
        }

        public void Dispose() {
        }
    }

    public class Phd2GetStarImage : Phd2Method {
        public override string Id => "97";

        public override string Method => "get_star_image";
    }

    //public class Phd2SizeParameter {
    //    [JsonProperty(PropertyName = "size")]
    //    public int Size { get; set; }
    //}

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