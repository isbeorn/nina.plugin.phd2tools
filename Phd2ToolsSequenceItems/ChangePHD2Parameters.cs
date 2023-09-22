using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "Change PHD2 Parameters")]
    [ExportMetadata("Description", "This item will change one of the available parameters for PHD2")]
    [ExportMetadata("Icon", "SettingsSVG")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class ChangePHD2Parameters : SequenceItem {
        private IGuiderMediator guiderMediator;
        private IProfileService profileService;

        [ImportingConstructor]
        public ChangePHD2Parameters(IGuiderMediator guiderMediator, IProfileService profileService) {
            this.guiderMediator = guiderMediator;
            this.profileService = profileService;

            ditherPixels = profileService.ActiveProfile.GuiderSettings.DitherPixels;
            ditherRAOnly = profileService.ActiveProfile.GuiderSettings.DitherRAOnly;
            settlePixels = profileService.ActiveProfile.GuiderSettings.SettlePixels;
            settleTime = profileService.ActiveProfile.GuiderSettings.SettleTime;
            settleTimeout = profileService.ActiveProfile.GuiderSettings.SettleTimeout;
            roiPct = profileService.ActiveProfile.GuiderSettings.PHD2ROIPct;
        }

        public ChangePHD2Parameters(ChangePHD2Parameters copyMe) : this(copyMe.guiderMediator, copyMe.profileService) {
            CopyMetaData(copyMe);
        }

        [ObservableProperty]
        private IList<string> issues = new List<string>();

        public override object Clone() {
            return new ChangePHD2Parameters(this) {
                Phd2Parameter = this.Phd2Parameter,
                DitherPixels = this.DitherPixels,
                DitherRAOnly = this.DitherRAOnly,
                SettlePixels = this.SettlePixels,
                SettleTime = this.SettleTime,
                RoiPct = this.RoiPct,
            };
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            switch (Phd2Parameter) {
                case PHD2Parameter.DitherPixels: {
                        profileService.ActiveProfile.GuiderSettings.DitherPixels = DitherPixels;
                        break;
                    }
                case PHD2Parameter.DitherRAOnly: {
                        profileService.ActiveProfile.GuiderSettings.DitherRAOnly = DitherRAOnly;
                        break;
                    }
                case PHD2Parameter.SettlePixelTolerance: {
                        profileService.ActiveProfile.GuiderSettings.SettlePixels = SettlePixels;
                        break;
                    }
                case PHD2Parameter.MinimumSettleTime: {
                        profileService.ActiveProfile.GuiderSettings.SettleTime = SettleTime;
                        break;
                    }
                case PHD2Parameter.SettleTimeout: {
                        profileService.ActiveProfile.GuiderSettings.SettleTimeout = SettleTimeout;
                        break;
                    }
                case PHD2Parameter.ROIPercentage: {
                        profileService.ActiveProfile.GuiderSettings.PHD2ROIPct = RoiPct;
                        break;
                    }
            }
        }

        [ObservableProperty]
        [property: JsonProperty]
        private PHD2Parameter phd2Parameter;

        [ObservableProperty]
        [property: JsonProperty]
        private double ditherPixels;

        [ObservableProperty]
        [property: JsonProperty]
        private bool ditherRAOnly;

        [ObservableProperty]
        [property: JsonProperty]
        private double settlePixels;

        [ObservableProperty]
        [property: JsonProperty]
        private int settleTime;

        [ObservableProperty]
        [property: JsonProperty]
        private int settleTimeout;

        [ObservableProperty]
        [property: JsonProperty]
        private int roiPct;

        public bool Validate() {
            var i = new List<string>();
            var info = guiderMediator.GetInfo();
            if (!info.Connected) {
                i.Add(Loc.Instance["LblGuiderNotConnected"]);
            } else {
                if (!(guiderMediator.GetDevice() is PHD2Guider)) {
                    i.Add("Connected guider has to be PHD2");
                }
            }
            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(ChangePHD2Parameters)}";
        }
    }

    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum PHD2Parameter {

        [Description("LblDitherPixels")]
        DitherPixels,

        [Description("LblDitherRAOnly")]
        DitherRAOnly,

        [Description("LblSettlePixelTolerance")]
        SettlePixelTolerance,

        [Description("LblMinimumSettleTime")]
        MinimumSettleTime,

        [Description("LblSettleTimeout")]
        SettleTimeout,

        [Description("LblPHD2ROIPct")]
        ROIPercentage
    }
}