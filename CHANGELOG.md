# Phd2 Tools

## 1.0.3.0
- Added an `Exposure Time` option to the `Change PHD2 Parameters` instruction. This allows users to adjust the guider's exposure time.

## 1.0.2.0
- Fixed: InterruptWhenRMSAbove and ChangePHD2Parameter did not save and restore user input
- Fixed: Background worker of InterruptWhenRMSAbove was not cancelled properly

## 1.0.1.0
- Added new trigger "RestartWhenSaturated". It will monitor the guide pulses and if a saturated star is detected the trigger will restart guiding where PHD2 can reselect a new star that is not saturated. Make sure your PHD2 settings are set to not select saturated stars, otherwise this trigger won't work.

## 1.0.0.0
- Initial release