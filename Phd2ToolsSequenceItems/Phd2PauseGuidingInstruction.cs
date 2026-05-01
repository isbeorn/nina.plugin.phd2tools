using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "PHD2 Pause Guiding")]
    [ExportMetadata("Description", "Pauses PHD2 guide corrections without losing the lock position. Optionally also pauses looping exposures.")]
    [ExportMetadata("Icon", "PhdTools_Settle")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Phd2PauseGuidingInstruction : SequenceItem, IValidatable {

        private IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public Phd2PauseGuidingInstruction(IGuiderMediator guiderMediator) {
            this.guiderMediator = guiderMediator;
        }

        public Phd2PauseGuidingInstruction(Phd2PauseGuidingInstruction copyMe) : this(copyMe.guiderMediator) {
            CopyMetaData(copyMe);
            PauseLooping = copyMe.PauseLooping;
        }

        [ObservableProperty]
        [property: JsonProperty]
        private bool pauseLooping = false;

        [ObservableProperty]
        private IList<string> issues = new List<string>();

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!(guiderMediator.GetDevice() is PHD2Guider phd2Guider)) {
                throw new SequenceEntityFailedException("Connected guider is not PHD2");
            }

            Array parameters = PauseLooping
                ? (Array)(new object[] { true, "full" })
                : (Array)(new bool[] { true });

            var msg = new Phd2Pause { Parameters = parameters };
            var response = await phd2Guider.SendMessage(msg);

            if (response?.error != null && !string.IsNullOrEmpty(response.error.message)) {
                throw new SequenceEntityFailedException($"PHD2 set_paused failed: {response.error.message}");
            }

            Logger.Info($"PHD2 guiding paused (PauseLooping={PauseLooping})");
        }

        public override object Clone() {
            return new Phd2PauseGuidingInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(Phd2PauseGuidingInstruction)}, PauseLooping: {PauseLooping}";
        }

        public bool Validate() {
            var i = new List<string>();
            bool valid = true;
            if (!guiderMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblGuiderNotConnected"]);
                valid = false;
            } else if (!(guiderMediator.GetDevice() is PHD2Guider)) {
                i.Add("Connected guider is not PHD2");
                valid = false;
            }
            Issues = i;
            return valid;
        }
    }
}
