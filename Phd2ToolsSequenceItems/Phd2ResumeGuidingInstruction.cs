using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
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

    [ExportMetadata("Name", "PHD2 Resume Guiding")]
    [ExportMetadata("Description", "Resumes PHD2 guide corrections. Optionally refreshes the lock position to the actual current star position (useful after mechanical shifts e.g. focuser moves at filter changes).")]
    [ExportMetadata("Icon", "PhdTools_Settle")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Phd2ResumeGuidingInstruction : SequenceItem, IValidatable {

        private IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public Phd2ResumeGuidingInstruction(IGuiderMediator guiderMediator) {
            this.guiderMediator = guiderMediator;
        }

        public Phd2ResumeGuidingInstruction(Phd2ResumeGuidingInstruction copyMe) : this(copyMe.guiderMediator) {
            CopyMetaData(copyMe);
            ReacquireLock = copyMe.ReacquireLock;
            FallbackToFindStar = copyMe.FallbackToFindStar;
        }

        [ObservableProperty]
        [property: JsonProperty]
        private bool reacquireLock = true;

        [ObservableProperty]
        [property: JsonProperty]
        private bool fallbackToFindStar = true;

        [ObservableProperty]
        private IList<string> issues = new List<string>();

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!(guiderMediator.GetDevice() is PHD2Guider phd2Guider)) {
                throw new SequenceEntityFailedException("Connected guider is not PHD2");
            }

            if (ReacquireLock) {
                await RefreshLockPosition(phd2Guider, token);
            }

            var resumeMsg = new Phd2Pause { Parameters = new bool[] { false } };
            var resumeResponse = await phd2Guider.SendMessage(resumeMsg);

            if (resumeResponse?.error != null && !string.IsNullOrEmpty(resumeResponse.error.message)) {
                throw new SequenceEntityFailedException($"PHD2 set_paused(false) failed: {resumeResponse.error.message}");
            }

            Logger.Info($"PHD2 guiding resumed (ReacquireLock={ReacquireLock})");
        }

        private async Task RefreshLockPosition(PHD2Guider phd2Guider, CancellationToken token) {
            var getLockMsg = new Phd2GetLockPosition();
            var getLockResp = await phd2Guider.SendMessage<GetLockPositionResponse>(getLockMsg);

            if (getLockResp?.result != null && getLockResp.result.Length >= 2) {
                float x = getLockResp.result[0];
                float y = getLockResp.result[1];

                var setLockMsg = new Phd2SetLockPosition {
                    Parameters = new object[] { x, y, false }
                };
                var setLockResp = await phd2Guider.SendMessage(setLockMsg);

                if (setLockResp?.error != null && !string.IsNullOrEmpty(setLockResp.error.message)) {
                    Logger.Warning($"PHD2 set_lock_position warning: {setLockResp.error.message}");
                } else {
                    Logger.Info($"PHD2 lock position refreshed near ({x:F1}, {y:F1})");
                }
            } else {
                Logger.Warning("PHD2 Resume: get_lock_position returned null");

                if (FallbackToFindStar) {
                    Notification.ShowWarning("PHD2: keine Lock-Position vorhanden, find_star als Fallback");
                    var findMsg = new Phd2FindStar { Parameters = new Phd2FindStarParameter() };
                    var findResp = await phd2Guider.SendMessage(findMsg);
                    if (findResp?.error != null && !string.IsNullOrEmpty(findResp.error.message)) {
                        throw new SequenceEntityFailedException(
                            $"PHD2 Resume: no lock position and find_star failed: {findResp.error.message}");
                    }
                    Logger.Info("PHD2 find_star recovery succeeded");
                } else {
                    throw new SequenceEntityFailedException(
                        "PHD2 Resume: no lock position available (FallbackToFindStar disabled)");
                }
            }
        }

        public override object Clone() {
            return new Phd2ResumeGuidingInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(Phd2ResumeGuidingInstruction)}, ReacquireLock: {ReacquireLock}";
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
