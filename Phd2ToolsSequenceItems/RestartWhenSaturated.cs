using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Equipment.MyGuider.PHD2.PhdEvents;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Plugin.Phd2Tools.Dockables;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Mediator;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "Restart When Saturated")]
    [ExportMetadata("Description", "This will trigger a guiding restart when a star is detected as saturated (due to high clouds or worsening seeing conditions). Make sure that your PHD2 Star Selection parameters do not allow star saturation.")]
    [ExportMetadata("Icon", "PhdTools_Eye")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class RestartWhenSaturated : SequenceTrigger {
        private IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public RestartWhenSaturated(IGuiderMediator guiderMediator) {
            this.guiderMediator = guiderMediator;
        }

        [ObservableProperty]
        private bool saturated = false;

        public RestartWhenSaturated(RestartWhenSaturated copyMe) : this(copyMe.guiderMediator) {
            CopyMetaData(copyMe);
        }

        public override object Clone() {
            return new RestartWhenSaturated(this);
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            await guiderMediator.StopGuiding(token);
            await guiderMediator.StartGuiding(false, progress, token);
            Saturated = false;
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (nextItem == null) { return false; }
            if (!(nextItem is IExposureItem exposureItem)) { return false; }
            if (exposureItem.ImageType != "LIGHT") { return false; }

            return Saturated;
        }

        public override void SequenceBlockStarted() {
            this.RegisterGuideEvent();
            base.SequenceBlockStarted();
        }

        public override void SequenceBlockFinished() {
            this.RegisterGuideEvent();
            base.SequenceBlockFinished();
        }

        private void GuiderMediator_GuideEvent(object sender, NINA.Core.Interfaces.IGuideStep e) {
            if (e is PhdEventGuideStep guideStep == false) {
                return;
            }

            switch (guideStep.ErrorCode) {
                case 0:
                    if (Saturated) {
                        Logger.Info($"Star saturation recovered.");
                        Saturated = false;
                    }
                    break;

                case 1:
                    if (!Saturated) {
                        Logger.Info($"Star saturation detected. Restarting guiding if not recovering before next LIGHT exposure.");
                        Saturated = true;
                    }
                    break;

                default:
                    return;
            }
        }

        private void RegisterGuideEvent() {
            guiderMediator.GuideEvent -= GuiderMediator_GuideEvent;
            if (guiderMediator.GetDevice() is PHD2Guider) {
                if (ItemUtility.IsInRootContainer(Parent)) {
                    guiderMediator.GuideEvent += GuiderMediator_GuideEvent;
                }
            }
        }

        public override void AfterParentChanged() {
            this.RegisterGuideEvent();
            base.AfterParentChanged();
        }
    }
}