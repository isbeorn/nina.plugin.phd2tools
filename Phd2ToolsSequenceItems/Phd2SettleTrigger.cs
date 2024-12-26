
using CsvHelper;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Mediator;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "Settle before Exposure")]
    [ExportMetadata("Description", "This trigger will ensure guiding has settled before starting anexposure")]
    [ExportMetadata("Icon", "PhdTools_Settle")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]


    public class Phd2SettleTrigger : SequenceTrigger, IValidatable {

        [ImportingConstructor]
        public Phd2SettleTrigger(IGuiderMediator guiderMediator, IProfileService profileService) {
            this.guiderMediator = guiderMediator;
            this.profileService = profileService;
        }

        public Phd2SettleTrigger(Phd2SettleTrigger copyMe) : this(copyMe.guiderMediator, copyMe.profileService) {
            CopyMetaData(copyMe);
        }

        protected IGuiderMediator guiderMediator;

        protected IProfileService profileService;

        protected IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override object Clone() {
            return new Phd2SettleTrigger(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(Phd2SettleTrigger)}";
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            await Phd2SettleHelper.getInstance(guiderMediator).WaitForSettle(profileService, progress, token);
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (nextItem is IExposureItem && ((IExposureItem)nextItem).ImageType == CaptureSequence.ImageTypes.LIGHT) {
                // This trigger is to be run only when the next sequcne item is a light sub
                return true;
            } else {
                return false;
            }
        }

        public bool Validate() {
            bool validated = true;
            var i = new List<string>();
            if (!guiderMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblGuiderNotConnected"]);
                validated = false;
            } else if (!(guiderMediator.GetDevice() is PHD2Guider)) {
                i.Add("Connected guider is not PHD2");
                validated = false;
            } else if (!Phd2SettleHelper.getInstance(guiderMediator).IsValidPHD2Guider()) {
                i.Add("Guider class not of the expected PHDGuider type");
                validated = false;
            }
            Issues = i;
            return validated;
        }
    }
}