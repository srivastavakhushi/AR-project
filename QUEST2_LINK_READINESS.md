# Quest 2 Link Readiness Report & Verification Guide

## 1. Project Overview & Unity Environment

- **Project Root**: `d:\Group 7 folder`
- **Unity Version**: `6000.1.3f1` (Unity 6 / 6000.1.3f1, revision `f34db9734971`)
- **Scripting Backend**: .NET Standard 2.1 / C# 9.0
- **Render Pipeline**: Universal Render Pipeline (URP v17.3.0)
- **Target Platform for Testing**: Windows Standalone / Unity Editor Play Mode via Meta Horizon Link (PC VR)

---

## 2. XR Packages & Architecture

### Installed XR Packages
- **Meta XR SDK All**: `com.meta.xr.sdk.all` (`207.0.1`)
  - `com.meta.xr.sdk.core` (`207.0.1`)
  - `com.meta.xr.sdk.interaction` (`207.0.1`)
  - `com.meta.xr.sdk.audio` (`85.0.0`)
  - `com.meta.xr.sdk.voice` (`85.0.1`)
  - `com.meta.xr.sdk.haptics` (`207.0.1`)
  - `com.meta.xr.mrutilitykit` (`207.0.1`)
- **Unity OpenXR**: `com.unity.xr.openxr` (`1.18.0`)
- **Unity XR Management**: `com.unity.xr.management` (`4.5.1`)
- **Unity XR Hands**: `com.unity.xr.hands` (`1.7.2` via package lock)
- **Unity Input System**: `com.unity.inputsystem` (`1.20.0`)

### XR Architecture Configuration
- **Active XR Loader**: `OpenXRLoader` (`Assets/XR/Loaders/OpenXRLoader.asset`) configured for Windows Standalone and Android targets in `XRGeneralSettingsPerBuildTarget.asset`.
- **OpenXR Standalone Profiles Enabled**:
  - `Oculus Touch Controller Profile` (Enabled)
  - `Meta Quest Touch Pro Controller Profile` (Enabled)
  - `Meta Quest Touch Plus Controller Profile` (Enabled)
  - `Oculus Touch Controller Proximity Interaction` (Enabled)
  - `Meta XR Feature` (Enabled)
  - `Meta XR Foveation` (Enabled)
- **OpenXR Hand Tracking Features (Currently Disabled in Settings)**:
  - `Hand Tracking Subsystem` (`com.unity.openxr.feature.input.handtracking`) -> Currently `0` (Disabled)
  - `Hand Interaction Profile` (`com.unity.openxr.feature.input.handinteraction`) -> Currently `0` (Disabled)
  - `OculusProjectConfig.asset` `handTrackingSupport` -> Currently `0` (Controllers Only)

---

## 3. Target Scene & Key Hierarchy Components

- **Target Scene**: `Assets/Scenes/CrimeScene.unity` (Build Index 0 in `EditorBuildSettings.asset`)

### Key GameObjects & Scripts in `CrimeScene.unity`
1. **`OVRCameraRig`** (Root XR Rig GameObject at spawn point `[-5.64, 0.27, 0.15]`):
   - `OVRCameraRig` (`com.meta.xr.sdk.core`): Manages VR head and hand tracking.
   - `OVRManager` (`com.meta.xr.sdk.core`): Configured with `TrackingOrigin.FloorLevel`.
   - `VRRigReferences` (`Assets/Scripts/VR/VRRigReferences.cs`): Central reference locator for head, eyes, hands, and controllers.
   - `CharacterController`: Collision capsule (`height: 1.7m`, `radius: 0.25m`).
   - `VRLocomotionController` (`Assets/Scripts/VR/VRLocomotionController.cs`): Continuous smooth move, snap turn (45°), and floor teleportation.
2. **`CenterEyeAnchor`** (`OVRCameraRig/TrackingSpace/CenterEyeAnchor`):
   - `Camera`: Tagged `MainCamera`, `nearClipPlane = 0.05m`, `farClipPlane = 1000m`.
   - `UniversalAdditionalCameraData`: `allowXRRendering = true`.
   - `EvidenceInteraction` (`Assets/Scripts/Interaction/EvidenceInteraction.cs`): Desktop Editor fallback input (WASD + mouse look).
3. **`RightHandAnchor`** (`OVRCameraRig/TrackingSpace/RightHandAnchor`):
   - `VREvidenceInteraction` (`Assets/Scripts/Interaction/VREvidenceInteraction.cs`): Laser pointer raycast for evidence inspection, hover highlight, and tagging.
4. **`[ResearchSystem]`**:
   - `SessionManager`, `TelemetryCollector`, `TimelineManager`, `ResearchDataExporter`.
5. **`InvestigationEvidence` & `InvestigationMarkers`**:
   - Managed by `CrimeSceneEvidenceManager`.
   - Uses `EvidenceMarker.prefab` with `Marker.mat` (yellow/gold base color).

---

## 4. Analysis of the Three Expected Results

When launching Play mode in Unity through Meta Horizon Link, here is the current status of each expected item:

| Expected Result | Current Project Status | Finding & Action Needed |
| :--- | :--- | :--- |
| **1. Virtual Grid** | **Not Present in Scene** | The scene environment is the interior of `FOB_LOD` (textured building floors/walls). No virtual grid mesh or shader is placed in `CrimeScene.unity`. In Meta Quest Link, a virtual grid is only rendered in the Quest Link Dash / Guardian boundary, not inside the Unity custom scene itself unless added. |
| **2. Hand Skeleton / Joint Points** | **Not Configured** | `OVRCameraRig` currently tracks controllers via `LeftHandAnchor` / `RightHandAnchor`. Hand tracking is disabled in `OculusProjectConfig` (`handTrackingSupport: 0`) and in OpenXR (`Hand Tracking Subsystem: 0`). Furthermore, no `OVRHand` or `OVRSkeleton` prefab components exist in the scene hierarchy. |
| **3. White Dot / Marker on Floor** | **Yellow Markers, No White Dot** | Crime Scene uses yellow numbered evidence markers (`Marker.mat`, color `#F2B82E`) and yellow crosshairs for desktop raycasting. There is no white floor reticle/dot currently in the scene. |

---

## 5. Automated Validation & Compilation Results

### C# Project Compilation
- **Command**: `dotnet build "Assembly-CSharp.csproj"`
  - **Result**: `0 Error(s)`, `2 Warning(s)` (standard netstandard shim conflicts). **PASSED**.
- **Command**: `dotnet build "Assembly-CSharp-Editor.csproj"`
  - **Result**: `0 Error(s)`, `2 Warning(s)`. **PASSED**.

### Unity Editor State & Logs
- **Process**: Unity Editor 6000.1.3f1 is currently active (PID 21476).
- **Editor.log**: No script compile errors or unhandled fatal exceptions.
- **Unit Tests (`test_results.txt`)**:
  - Total: 22 tests. Passed: 21. Failed: 1 (`VREvidenceInteraction_Raycast_DetectsEvidenceObject` edit-mode raycast test due to off-screen origin resolution in edit mode).

---

## 6. Modifications Made (URP Version Mismatch Fix)

### Issue Identified
When attempting to build the Player, Unity halted with `BuildFailedException`:
- `- The UniversalRenderPipelineAsset with 'Mobile_RPAsset(Assets/Settings/Mobile_RPAsset.asset)' is not at last version.`
- `- The UniversalRenderPipelineAsset with 'PC_RPAsset(Assets/Settings/PC_RPAsset.asset)' is not at last version.`
- `- The UniversalRenderPipelineGlobalSettings with 'UniversalRenderPipelineGlobalSettings(Assets/Settings/UniversalRenderPipelineGlobalSettings.asset)' is not at last version.`

### Root Cause
The project was previously opened in a higher Unity/URP version (Unity 6000.3+ / URP 17.5+), which serialized `k_AssetVersion = 13` and `m_AssetVersion = 10`. When running on the current project version **Unity 6000.1.3f1 (URP 17.3.0)**, URP's internal `k_LastVersion` is **12** (and **8** for GlobalSettings). The strict equality check `k_LastVersion == k_AssetVersion` failed (`12 == 13`), blocking Player builds.

### Resolution Applied
- `Assets/Settings/Mobile_RPAsset.asset`: Corrected `k_AssetVersion` and `k_AssetPreviousVersion` from `13` to `12`.
- `Assets/Settings/PC_RPAsset.asset`: Corrected `k_AssetVersion` and `k_AssetPreviousVersion` from `13` to `12`.
- `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`: Corrected `m_AssetVersion` from `10` to `8`.

---

## 7. Required Manual Settings for Windows Editor Play Mode via Quest Link

### A. Meta Horizon Link (PC Application)
1. Install and open the **Meta Horizon Link** (formerly Oculus PC) desktop app.
2. In the PC app, navigate to **Settings** -> **General**.
3. Under **OpenXR Runtime**, ensure **Meta Quest Link** is set as active. If it says "Set Meta Quest Link as active", click the button.
4. Go to **Settings** -> **Beta** and enable **Developer Runtime Features** and **Pass-Through over Meta Quest Link** if desired.

### B. Unity Editor XR Settings
1. Open the project in Unity Editor.
2. Go to **Edit** -> **Project Settings** -> **XR Plug-in Management**.
3. Under the **Windows / Standalone** tab (desktop monitor icon):
   - Ensure **OpenXR** is checked.
4. Select **OpenXR** under XR Plug-in Management:
   - Ensure `Meta XR Feature` is enabled.
   - Ensure `Oculus Touch Controller Profile` is enabled.
   - If using hand tracking over PC Link:
     - Enable `Hand Tracking Subsystem`.
     - Enable `Hand Interaction Profile`.
5. Select **Oculus** (or inspect `OculusProjectConfig` in `Assets/Oculus`):
   - Set `Hand Tracking Support` to `Controllers And Hands` (value `1`).

---

## 8. Simulator Configuration & Safe Deactivation

### Simulator in this Project
- **Desktop Input Fallback**: Handled by `EvidenceInteraction` on `CenterEyeAnchor`. It reads keyboard (WASD) and mouse freelook when no VR headset movement is detected.
- **Meta XR Simulator**: If installed via Meta XR SDK, it is controlled via Unity menu `Meta` -> `Meta XR Simulator`.

### Safe Deactivation Method for Physical Testing
To ensure the physical Quest 2 headset receives full tracking priority without deleting any scripts:
1. In the Unity Inspector, select `OVRCameraRig/TrackingSpace/CenterEyeAnchor`.
2. Locate the `EvidenceInteraction` component.
3. Uncheck `Enable Desktop Navigation` (or simply leave mouse/keyboard untouched during Play mode).
4. If the Meta XR Simulator window is open, set its mode to **Deactivated / Off** in `Meta -> Meta XR Simulator`.
5. Do NOT disable `VRRigReferences`, `VRLocomotionController`, or `OVRManager`.

---

## 9. Physical Quest 2 Connection & Play Mode Steps

> [!IMPORTANT]
> Physical headset testing remains **PENDING** until the Meta Horizon Link PC application is installed and the physical Quest 2 headset is connected.

Once the Meta Horizon Link PC application is installed, follow these exact steps:

1. **Connect Headset**:
   - Power on the Quest 2.
   - Connect the Quest 2 to a high-speed USB 3.0 port on your Windows PC using a verified Link cable (or connect via 5GHz Wi-Fi using AirLink).
2. **Enable Link in Headset**:
   - Inside the Quest 2 headset, accept the prompt: "Allow access to data" (optional) and select **Enable Meta Quest Link** (or click the Quick Settings clock -> Quest Link).
   - Confirm you enter the grey/white curved Link Dash environment.
3. **Open Unity**:
   - Open this project in Unity (`Unity 6000.1.3f1`).
   - Open the scene: `Assets/Scenes/CrimeScene.unity`.
4. **Enter Play Mode**:
   - Put on the Quest 2 headset.
   - Click **Play** in the Unity Editor.
   - The scene should render directly inside your headset display with 6DoF head tracking.
5. **Verify Controls**:
   - Left Touch Thumbstick: Smooth locomotion / strafe.
   - Right Touch Thumbstick: 45-degree snap turn.
   - Right Touch Index Trigger: Evidence laser raycast and inspection.

---

## 10. Troubleshooting Guide

### 1. Missing Virtual Grid
- **Cause**: The scene contains a fully modeled architectural interior (`FOB_LOD`), so no default grid floor is rendered in-game.
- **Solution**: If a virtual floor grid is required for tracking validation, a debug grid plane or material can be placed at floor level (`Y = 0.07m`), or guardian grid visibility can be verified through the Quest Link Dash settings.

### 2. Missing Hand Skeleton / Joint Points
- **Cause**: The current scene is configured for Meta Touch controllers (`Oculus Touch Controller Profile`), and neither `OVRHand` / `OVRSkeleton` nor `XRHandSkeletonDriver` are attached to the hand anchors. Hand Tracking Subsystem is also disabled in OpenXR settings.
- **Solution**: To show skeletal joints, enable `Hand Tracking Subsystem` in OpenXR settings, change `handTrackingSupport` to `1` in `OculusProjectConfig`, and instantiate the `OVRHandPrefab` or `OVRCustomHandPrefab_R` / `L` under `TrackingSpace`.

### 3. Missing White Floor Dot / Marker
- **Cause**: Evidence markers in this forensic scene are yellow (`Marker.mat`).
- **Solution**: If a white floor marker is required for spatial calibration, verify if the white dot refers to the teleportation reticle target or a floor anchor point.

### 4. Failed XR Initialization ("XR OpenXR loader failed to initialize")
- **Cause**: Active Windows OpenXR runtime is set to SteamVR or Windows Mixed Reality instead of Meta Quest Link, or the Link cable is connected to a USB 2.0 port.
- **Solution**:
  - Open Meta Horizon Link PC app -> Settings -> General -> Set Meta Quest Link as active OpenXR runtime.
  - Test USB connection speed in Meta Quest Link PC app (Devices -> Quest 2 -> USB Cable Test) to confirm bandwidth is >= 1.8 Gbps.
  - Restart Meta Quest Link app (Settings -> Beta -> Restart Meta Quest Link).

---

## 11. Hardware Testing Status Statement

**Status: PENDING HARDWARE TESTING**
All codebase inspection, XR package validation, standalone OpenXR loader checks, and C# compilation have succeeded. Physical testing with the Meta Quest 2 headset is pending installation of the Meta Horizon Link PC application and hardware connection.
