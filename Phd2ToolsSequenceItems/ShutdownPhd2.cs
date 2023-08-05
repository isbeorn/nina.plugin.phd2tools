using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Guider;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "Shutdown PHD2")]
    [ExportMetadata("Description", "This item will disconnect from PHD2 and shutdown the PHD2 instance")]
    [ExportMetadata("Icon", "PowerSVG")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class ShutdownPhd2Instruction : SequenceItem, IValidatable {
        private IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public ShutdownPhd2Instruction(IGuiderMediator guiderMediator) {
            this.guiderMediator = guiderMediator;
        }

        public ShutdownPhd2Instruction(ShutdownPhd2Instruction copyMe) : this(copyMe.guiderMediator) {
            CopyMetaData(copyMe);
        }

        [ObservableProperty]
        private IList<string> issues = new List<string>();

        public override object Clone() {
            return new ShutdownPhd2Instruction(this);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (guiderMediator.GetDevice() is PHD2Guider pHD2Guider) {
                await pHD2Guider.SendMessage(new Phd2Shutdown());
                await guiderMediator.Disconnect();
            }
        }

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
            return $"Category: {Category}, Item: {nameof(ShutdownPhd2Instruction)}";
        }
    }
}