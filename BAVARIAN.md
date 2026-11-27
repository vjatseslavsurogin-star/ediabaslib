# Bavarian Diagnostics 0.4 — USB preview

Based on [Deep OBD / EdiabasLib](https://github.com/uholeschak/ediabaslib), Ulrich Holeschak, GPL-3.0. Original license and source remain in the repository.

This branch builds a debug APK with package ID `org.bavarian.diagnostics` (separate from the original app). The launcher shows a USB hardware check and ECU asset status. USB enumeration does not send diagnostic requests. A recognized VID/PID is not proof of a functioning K+DCAN cable or vehicle communication.

## Missing ECU data
Upstream `BmwDeepObd/ReadMe.txt` requires the matching encrypted ECU asset in `BmwDeepObd/Assets/Ecu.bin` (or its matching split asset set). The original code requires its presence and verifies the package; these checks have not been disabled. Obtain the matching package with authorization from the upstream project/data provider. Do not commit proprietary diagnostic data to this public fork, and do not bypass license, encryption or validation checks. A local build with authorized assets can use the same dotnet build settings as the workflow. The public CI artifact is only a hardware preview unless valid data is provided. Keep at least 3.4 GB of free storage for original ECU extraction.

## First test
1. Download the latest successful artifact from the branch Actions page. Extract the signed debug APK.
2. Install on an Android phone (Android 7+ according to the project, actual compatibility untested).
3. Connect the FTDI K+DCAN adapter via OTG. Refresh hardware check; record VID/PID.
4. Do not use any coding, actuator or firmware-writing features. The inherited diagnostic engine contains write-capable functions and is NOT yet a read-only restricted implementation.
5. Vehicle diagnostics are not verified; they require authorized ECU data and actual on-vehicle tests.

CI disables bundled PIC adapter updates for the USB baseline. No firmware placeholders are used. No connection or vehicle signals are simulated by the launcher.

## 0.5: local ECU library (offline)
Import legally obtained BMW EDIABAS .PRG and .GRP files from Android's system file picker. Files are copied into private storage, not uploaded. Existing names are not overwritten. Use a single matching ECU data release; do not mix variants. The inspector uses EdiabasNet with no hardware interface and NoInitForVJobs=true to enumerate _JOBS for individual PRG files, without executing any actual ECU job. Group identification is not executed. This is separate from the encrypted upstream asset: no checks are bypassed. Import is not a live diagnostic connection and no compatibility with an actual DDE is yet established. No proprietary ECU binaries are distributed. To obtain your own files, consult the authorized BMW Standard Tools installation's EDIABAS/ECU directory and confirm the DDE/EGS variant with a diagnostic tool. The debug build is not yet tested on Android hardware.

## 0.6: language settings
Bavarian launcher and ECU library have an independent persisted English/Russian UI setting (English default). The inherited diagnostic engine is configured for English; known preview errors use English and raw external exception text is not shown in the preview. Raw ECU job names, DTC identifiers and manufacturer-supplied descriptions are not translated by this feature. A verified English DTC mapping and live ECU testing are still required. System Android dialogs may use the device language.

## 0.7: private preloaded ECU library
A separately assembled personal APK can carry an authorized ZIP in assets/bavarian_ecu.zip. The launcher imports the approved filenames into private app storage without overwriting existing files, validating size and file header and staging each file. Public CI builds do not contain this ZIP. This feature does not replace the original encrypted Deep OBD package or enable live diagnostics, and is for authorized local data only. Private APK packaging must re-sign the modified APK; a different signing key may require uninstalling the previous debug build (which deletes app data). No actual ECU data is committed to this repository.
