# MSIX Packaging for ScreenshotFlash

This folder contains the Windows Application Packaging Project used to generate the MSIX bundle that can be submitted to the Microsoft Store.

## Building the MSIX package

1. **Install prerequisites**
   - Visual Studio 2022 with the **.NET desktop development** and **Universal Windows Platform development** workloads.
   - The **MSIX Packaging Tools** and the **Windows 10 SDK 19041** (or newer).
2. Open `ScreenshotFlash.sln` in Visual Studio.
3. Set the solution configuration to `Release` and the platform to either `x86` or `x64` depending on the binary you want to produce.
4. Right-click the `ScreenshotFlash.Package` project and choose **Publish ➜ Create App Packages...**.
5. When asked, select **Microsoft Store** as the distribution channel and sign in with the partner account that owns the reserved Store name.
6. Provide the reserved Store identity (Publisher and Package Name). Visual Studio will update `Package.appxmanifest` and create the signing certificate automatically.
7. Complete the wizard to build the MSIX bundle. The resulting `.msixupload` can be found inside `AppPackages/`.

> **Note:** The project is configured to generate MSIX bundles for both `x86` and `x64`. If you need ARM64 support, add the platform both to the WinForms project and to this packaging project.

## Store assets

Provide the Microsoft Store image assets in `StoreAssets/` (this repository intentionally omits binary image placeholders). Add each required logo and screenshot at the correct resolution so that the package passes the Store validation checks.

## Signing the package

`AppxPackageSigningEnabled` is intentionally set to `False` inside the packaging project so that you can build unsigned artifacts locally. When you prepare the official Store submission, update the project to enable signing and provide the certificate issued for your Publisher ID or allow Visual Studio to generate a trusted certificate during the **Create App Packages** wizard.

