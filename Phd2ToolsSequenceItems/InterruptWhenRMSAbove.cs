using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    [ExportMetadata("Name", "Interrupt when RMS above")]
    [ExportMetadata("Description", "This trigger will interrupt an exposure when a guide pulse exceeds a certain value")]
    [ExportMetadata("Icon", "PhdTools_Eye")]
    [ExportMetadata("Category", "Phd2 Tools")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class InterruptWhenRMSAbove : SequenceTrigger {
        private IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public InterruptWhenRMSAbove(IGuiderMediator guiderMediator) {
            this.guiderMediator = guiderMediator;

            workerCts = new CancellationTokenSource();
        }

        public InterruptWhenRMSAbove(InterruptWhenRMSAbove copyMe) : this(copyMe.guiderMediator) {
            CopyMetaData(copyMe);
        }

        public override object Clone() {
            return new InterruptWhenRMSAbove(this) {
                RmsThreshold = this.RmsThreshold,
                Mode = this.Mode,
                MinimumPoints = this.MinimumPoints
            };
        }

        public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            // This trigger is not actively executing but rather a background watchdog
            return Task.CompletedTask;
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (previousItem is IExposureItem) {
                StopRecording();
            }

            if (nextItem is IExposureItem exp) {
                // Start recording and background work when exposure is about to start
                activeRMSRecording = guiderMediator.StartRMSRecording();
                var type = guiderMediator.GetType();
                var GetRMSRecording = type.GetMethod("GetRMSRecording");

                RmsInstance = guiderMediator.GetRMSRecording(activeRMSRecording);
                exposureItem = exp;

                _ = BackgroundWorker();
            }

            // This trigger is not actively executing but rather a background watchdog
            return false;
        }

        public override void AfterParentChanged() {
            if (!ItemUtility.IsInRootContainer(this.Parent)) {
                // When item is removed from sequencer
                StopRecording();
            }
            base.AfterParentChanged();
        }

        private void StopRecording() {
            // Stop recording and background work when exposure is finished
            if (activeRMSRecording != Guid.Empty) {
                guiderMediator.StopRMSRecording(activeRMSRecording);
            }
            activeRMSRecording = Guid.Empty;
            RmsInstance = null;
            try {
                workerCts?.Cancel();
            } catch { }
        }

        public override void SequenceBlockFinished() {
            StopRecording();
            base.SequenceBlockFinished();
        }

        [ObservableProperty]
        private double rmsThreshold = 1;

        [ObservableProperty]
        private int minimumPoints = 5;

        [ObservableProperty]
        private GuideInterrupteMode mode = GuideInterrupteMode.Peak;

        [ObservableProperty]
        private RMS rmsInstance;

        private Guid activeRMSRecording;
        private IExposureItem exposureItem;
        private CancellationTokenSource workerCts;

        private Task BackgroundWorker() {
            try {
                workerCts?.Cancel();
            } catch { }
            return Task.Run(async () => {
                workerCts = new CancellationTokenSource();
                while (!workerCts.IsCancellationRequested) {
                    try {
                        if (activeRMSRecording != Guid.Empty && guiderMediator.GetInfo().Connected) {
                            bool interruptExposure = false;

                            if (Mode == GuideInterrupteMode.Peak) {
                                if (Math.Abs(RmsInstance.PeakRA) * RmsInstance.Scale > RmsThreshold) {
                                    Notification.ShowInformation($"RA peak above threshold ({Math.Round(rmsInstance.PeakRA * rmsInstance.Scale, 2)} / {RmsThreshold}) - Interrupting current exposure");
                                    Logger.Info($"RA peak above threshold ({RmsInstance.PeakRA * RmsInstance.Scale} / {RmsThreshold})");
                                    interruptExposure = true;
                                }
                                if (Math.Abs(RmsInstance.PeakDec * RmsInstance.Scale) > RmsThreshold) {
                                    Notification.ShowInformation($"Dec peak above threshold ({Math.Round(RmsInstance.PeakDec * RmsInstance.Scale, 2)} / {RmsThreshold}) - Interrupting current exposure");
                                    Logger.Info($"Dec peak above threshold ({RmsInstance.PeakDec * RmsInstance.Scale} / {RmsThreshold})");
                                    interruptExposure = true;
                                }
                            } else if (Mode == GuideInterrupteMode.RMS && RmsInstance.DataPoints > MinimumPoints) {
                                if (Math.Abs(RmsInstance.Total) * RmsInstance.Scale > RmsThreshold) {
                                    Notification.ShowInformation($"Total RMS above threshold ({Math.Round(RmsInstance.Total * RmsInstance.Scale, 2)} / {RmsThreshold}) - Interrupting current exposure");
                                    Logger.Info($"Total RMS above threshold ({rmsInstance.Total * rmsInstance.Scale} / {RmsThreshold})");
                                    interruptExposure = true;
                                }
                                if (Math.Abs(RmsInstance.RA) * RmsInstance.Scale > RmsThreshold) {
                                    Notification.ShowInformation($"RA RMS above threshold ({Math.Round(RmsInstance.RA * RmsInstance.Scale, 2)} / {RmsThreshold}) - Interrupting current exposure");
                                    Logger.Info($"RA RMS above threshold ({RmsInstance.RA * RmsInstance.Scale} / {RmsThreshold})");
                                    interruptExposure = true;
                                }
                                if (Math.Abs(RmsInstance.Dec) * RmsInstance.Scale > RmsThreshold) {
                                    Notification.ShowInformation($"Dec RMS above threshold ({Math.Round(RmsInstance.Dec * RmsInstance.Scale, 2)} / {RmsThreshold}) - Interrupting current exposure");
                                    Logger.Info($"Dec RMS above threshold ({RmsInstance.Dec * RmsInstance.Scale} / {RmsThreshold})");
                                    interruptExposure = true;
                                }
                            }

                            if (interruptExposure) {
                                if (exposureItem != null && exposureItem.Status == NINA.Core.Enum.SequenceEntityStatus.RUNNING) {
                                    Logger.Info("Interrupting running exposure item");
                                    exposureItem.Skip();
                                }
                            }
                        }
                        await Task.Delay(1000, workerCts.Token);
                    } catch (OperationCanceledException) {
                    } catch (Exception ex) {
                        Logger.Error(ex);
                    }
                }
            });
        }

        /// <summary>
        /// This string will be used for logging
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            return $"Category: {Category}, Trigger: {nameof(InterruptWhenRMSAbove)}, Mode {Mode}, Threshold {RmsThreshold}, Points {MinimumPoints}";
        }
    }

    public enum GuideInterrupteMode {
        Peak,
        RMS
    }
}