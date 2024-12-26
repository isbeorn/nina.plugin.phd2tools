using NINA.Core.Model;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace nina.plugin.phd2tools.Phd2ToolsSequenceItems {

    // Helper class that implements the "wait until settled" semantics, via a static factory method

    public class Phd2SettleHelper {

        private PHD2Guider phd2Guider = null;
        private MethodInfo mWaitForSettling = null;
        private MethodInfo mGetROI = null;
        private MethodInfo mTryRefreshShiftLockParams = null;
        private MethodInfo mWaitForStarSelected = null;
        private MethodInfo mIsCalibrated = null;
        private MethodInfo mWaitForCalibrationFinished = null;
        private MethodInfo mWaitForGuidingStarted = null;

        private Phd2SettleHelper() {
        }

        private static readonly Lazy<Phd2SettleHelper> lazy = new Lazy<Phd2SettleHelper>(() => new Phd2SettleHelper());

        // Returns an instance of the class, by instantiating one if necessary
        // Also initializes all the MethodInfo objects of the class, which will be used to access the PHD2Guider private method by reflection

        public static Phd2SettleHelper getInstance(IGuiderMediator mediator) {
            Phd2SettleHelper instance = lazy.Value;
            // if the device is not PHD2, wipes out the reference to the phd2 guider and returns
            if (!(mediator.GetDevice() is PHD2Guider)) {
                instance.phd2Guider = null;
                return instance;
            }
            // if the current reference to the phd2 guider is the same as the one provided by the mediator, no further action is necessary
            if (instance.phd2Guider != null && instance.phd2Guider == mediator.GetDevice()) return instance;
            // sets the internal reference to the phd2 guider
            instance.phd2Guider = (PHD2Guider)mediator.GetDevice();
            // initialize the MethodInfo for the private methods to be called via reflection
            Type type = instance.phd2Guider.GetType();
            instance.mWaitForSettling = type.GetMethod("WaitForSettling", BindingFlags.NonPublic | BindingFlags.Instance);
            instance.mGetROI = type.GetMethod("GetROI", BindingFlags.NonPublic | BindingFlags.Instance);
            instance.mTryRefreshShiftLockParams = type.GetMethod("TryRefreshShiftLockParams", BindingFlags.NonPublic | BindingFlags.Instance);
            instance.mWaitForStarSelected = type.GetMethod("WaitForStarSelected", BindingFlags.NonPublic | BindingFlags.Instance);
            instance.mIsCalibrated = type.GetMethod("IsCalibrated", BindingFlags.NonPublic | BindingFlags.Instance);
            instance.mWaitForCalibrationFinished = type.GetMethod("WaitForCalibrationFinished", BindingFlags.NonPublic | BindingFlags.Instance);
            instance.mWaitForGuidingStarted = type.GetMethod("WaitForGuidingStarted", BindingFlags.NonPublic | BindingFlags.Instance);
            return instance;
        }

        // This is the method that implements the "wait until settled" logic. It mimics the logic in the PHD2Guider class (StartGuiding method)
        // and therefore needs to call a number of its private method via reflection

        public async Task<bool> WaitForSettle(IProfileService profileService, IProgress<ApplicationStatus> progress, CancellationToken token) {
            // If settling is already underway (e.g., started by another thread), go directly to the wait loop
            // othwerise, issue the "start guiding" command and wait for star selection/calibration (if necessary) to complete
            if (phd2Guider.Settling != true) {
                await (Task)mWaitForSettling.Invoke(phd2Guider, [progress, token]);
                int[] roi = await (Task<int[]>)mGetROI.Invoke(phd2Guider, null);
                var guideMsg = new Phd2Guide() {
                    Parameters = new Phd2GuideParameter() {
                        Settle = new Phd2Settle() {
                            Pixels = profileService.ActiveProfile.GuiderSettings.SettlePixels,
                            Time = profileService.ActiveProfile.GuiderSettings.SettleTime,
                            Timeout = profileService.ActiveProfile.GuiderSettings.SettleTimeout
                        },
                        Recalibrate = false,
                        Roi = roi
                    }
                };
                var guideMsgResponse = await phd2Guider.SendMessage(guideMsg);
                await (Task)mTryRefreshShiftLockParams.Invoke(phd2Guider, null);
                var starSelected = await (Task<bool>)mWaitForStarSelected.Invoke(phd2Guider, [progress, token]);
                if (!starSelected) return false;
                var isCalibrated = await (Task<bool>)mIsCalibrated.Invoke(phd2Guider, null);
                if (!isCalibrated) {
                    await Task.Delay(5000, token);
                    await (Task<bool>)mWaitForCalibrationFinished.Invoke(phd2Guider, [progress, token]);
                }
            }
            // Wait until settling has completed and guiding has begun
            var retryAfterSeconds = TimeSpan.FromSeconds(profileService.ActiveProfile.GuiderSettings.AutoRetryStartGuidingTimeoutSeconds);
            using (var cancelOnTimeoutOrParent = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                var timeout = Task.Delay(
                    retryAfterSeconds,
                cancelOnTimeoutOrParent.Token);
                var guidingHasBegun = (Task)mWaitForGuidingStarted.Invoke(phd2Guider, [progress, cancelOnTimeoutOrParent.Token]);
                if (await Task.WhenAny(timeout, guidingHasBegun) == guidingHasBegun) {
                    // Guiding has been started successfully in time
                    // Wait for phd2 to settle and exit
                    await (Task)mWaitForSettling.Invoke(phd2Guider, [progress, token]);
                    return true;
                }
                try { cancelOnTimeoutOrParent?.Cancel(); } catch { }
            }
            return false;
        }

        // Validate whether the logical connection to the PHD2 guider class was successful:
        // the reference is not null, points to a PHDGuider instance, and all the reflection MethodInfo
        // for the necessary methods have been obtained

        public bool IsValidPHD2Guider() {
            if (phd2Guider == null) return false;
            if (!(phd2Guider is PHD2Guider)) return false;
            if (mGetROI == null) return false;
            if (mIsCalibrated == null) return false;
            if (mTryRefreshShiftLockParams == null) return false;
            if (mWaitForCalibrationFinished == null) return false;
            if (mWaitForGuidingStarted == null) return false;
            if (mWaitForSettling == null) return false;
            if (mWaitForStarSelected == null) return false;
            return true;
        }
    }
}