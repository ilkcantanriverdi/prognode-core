# PROGNODE Mobile — VS Code workflow (no Android Studio UI required)

PROGNODE Mobile is Flutter/Dart and is edited in VS Code.

Required toolchain on Windows:

- Flutter SDK in PATH
- VS Code Flutter + Dart extensions
- JDK supported by the installed Flutter/Android tooling
- Android SDK command-line tools + platform-tools (`adb`) + a current Android platform/build-tools

Android Studio itself is not required as the editor. Android SDK components are still required to build an Android app.

After the toolchain is available:

```powershell
cd mobile\Prognode.Mobile
.\BOOTSTRAP_PLATFORM_FILES.ps1
flutter doctor
flutter devices
flutter run
```

For Galaxy S25 enable Developer options + USB debugging and accept the PC trust prompt.

Google Play production build later:

```powershell
flutter build appbundle --release
```

The same Dart source is used for iOS. Apple signing/App Store packaging requires a macOS/Xcode build environment; this can be a local Mac or macOS CI/cloud runner.
