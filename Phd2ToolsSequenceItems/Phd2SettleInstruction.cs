using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "PHD2 Wait for Settle")]
    [ExportMetadata("Description", "This item gets PHD2 to wait until guiding settles")]
    [ExportMetadata("Icon", "PhdTools_Settle")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class Phd2SettleInstruction : SequenceItem, IValidatable {

        [ImportingConstructor]
        public Phd2SettleInstruction(IProfileService profileService, IGuiderMediator guiderMediator) {
            this.profileService = profileService;
            this.guiderMediator = guiderMediator;
        }

        public Phd2SettleInstruction(Phd2SettleInstruction copyMe) : this(copyMe.profileService, copyMe.guiderMediator) {
            CopyMetaData(copyMe);
        }

        private IProfileService profileService;
        private IGuiderMediator guiderMediator;

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await Phd2SettleHelper.getInstance(guiderMediator).WaitForSettle(profileService, progress, token);
        }

        public override object Clone() {
            return new Phd2SettleInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(Phd2SettleInstruction)}";
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