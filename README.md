# TapeMeasure

TapeMeasure is an editor-only measuring and layout tool for **Kerbal Space Program 1**. It runs in the VAB and SPH and lets you place measurements directly on a vessel, keep multiple named measurements, inspect vessel dimensions, snap to useful vessel geometry, and edit measurements after they are created.

TapeMeasure is intended for building tasks such as checking vehicle width and height, wheelbase, engine spacing, payload clearances, wing geometry, attachment locations, and angles between structural points.

## Requirements

TapeMeasure requires:

- Kerbal Space Program 1.12.x
- ToolbarController
- ClickThroughBlocker

ToolbarController manages the TapeMeasure toolbar button and the user's stock/Blizzy toolbar preference.

## Installation

Copy the `TapeMeasure` folder into the game's `GameData` directory so the installed layout is:

```text
Kerbal Space Program/
└── GameData/
    └── TapeMeasure/
        ├── Plugins/
        ├── Textures/
        ├── TapeMeasure.version
        ├── License.md
        └── README.md
```

Install ToolbarController and ClickThroughBlocker separately if they are not already installed.

## Opening TapeMeasure

TapeMeasure starts with its main window hidden. Enter the VAB or SPH and click the TapeMeasure toolbar icon to open it. The toolbar icon changes while measurement mode is active. While measurement mode is active, TapeMeasure also uses a custom measurement cursor registered through KSP's cursor controller.

The main window also includes a **Show measurement labels in the editor view** toggle so editor labels can be enabled or hidden without opening Settings.

The main window contains collapsible sections for:

- Snapping
- Automatic vessel dimensions
- Measurement list
- Selected measurement

Click a section heading to expand or collapse it. The window resizes to fit the visible sections. The Selected Measurement section is shown only after a measurement has been explicitly selected.

## Measuring a distance

1. Click **New Distance**, or press **M** to enter measurement mode.
2. Click the first point on the vessel.
3. Move the mouse. A live preview line is drawn from the first point to the current mouse/snap target.
4. Click the second point.
5. The completed measurement is added to the Measurement List.

TapeMeasure shows the straight-line distance and the vessel-axis X, Y, and Z components. Free surface points are resolved against the visible Part mesh when possible, preventing recessed/internal colliders from pulling points below the visible skin.

Creating a measurement does not automatically select it. Click its row in the Measurement List when you want to highlight it or edit its details.

## Measuring an angle

1. Click **New Angle**.
2. Select point **A**.
3. Select point **B**, which is the angle vertex.
4. Select point **C**.

The resulting A-B-C angle is displayed in degrees, with B as the vertex.

## Measurement list and selection

The Measurement List contains all current distance and angle measurements. A measurement can be selected by clicking anywhere in its row except the red delete `X`; deleting a row does not select it first.

Selecting a row:

- highlights that measurement in the editor
- opens the Selected Measurement pane
- allows renaming, lock-mode changes, endpoint editing, and other selected-measurement actions

Use **Clear Selection** at the top-right of the Selected Measurement pane to remove the selection and editor highlight without deleting the measurement.

The Measurement List pane can be collapsed from its heading or from Settings. Clickable measurement values are shown as blue label-style controls; the per-row delete `X` is shown as a bold red label-style control. The Name and Lock columns are spaced for easier visual separation.

## Automatic vessel dimensions

The Automatic Vessel Dimensions pane shows the visible vessel envelope using active rendered part geometry:

- **Length (Z)**
- **Width (X)**
- **Height (Y)**
- **Bounding box (X × Y × Z)**

Dimensions refresh automatically when KSP reports that the editor ship has been modified. The pane can be collapsed from its heading or from Settings.

## Snapping

The main window has a collapsible **Snapping** pane containing the master snapping switch, all individual snap targets, snap radius, and vessel grid spacing. The same controls remain available in the Settings window and stay synchronized.

Snapping is controlled from the separate **TapeMeasure Settings** window. Multiple snap targets can be enabled at the same time; the nearest valid target within the configured snap radius is used.

Available snap targets are:

- **Part origin** — the selected part's transform origin
- **Attachment node** — normal stack/docking attachment nodes
- **Surface attachment point** — the part's surface-attachment node
- **Part center** — center of the part's visible render bounds
- **Vessel root** — root part origin
- **Center of Mass**
- **Center of Lift**
- **Center of Thrust**
- **Vessel Axis / Grid** — vessel X/Y/Z axes through the previous endpoint and a configurable vessel-local grid

The snap radius is configurable in screen pixels. Vessel grid spacing is also configurable.

Holding **Shift** temporarily enables the configured snap targets without changing the saved master snapping setting.

Snapping is used during normal point placement, the live preview line, and endpoint editing.

## Part-relative and vessel-relative measurements

Each measurement can use one of two lock modes:

- **Part-relative** — each endpoint follows the part it was placed on.
- **Vessel-relative** — endpoints remain fixed in vessel coordinates even if the originally clicked part is moved.

Changing the lock mode rebases the measurement at its current visible position so the endpoints do not jump.

## Editing endpoints

Select a completed measurement and click **Edit Endpoints**.

Click and drag an endpoint marker to reposition it. Distance measurements support A and B; angle measurements support A, B, and C. Snapping remains available while dragging.

Endpoint graphics do not use physics colliders, so they do not interfere with normal vessel raycasts.

## Symmetry-aware measurements

When **Create symmetry counterpart measurements** is enabled, TapeMeasure can create equivalent measurements on KSP symmetry counterparts. Symmetry-generated measurements are normal independent measurements afterward and can be renamed, edited, locked, copied, or deleted separately.

## Measurement guides

For a selected distance measurement, optional projected component guides can be displayed in vessel coordinates:

- **X** — red
- **Y** — green
- **Z** — blue

These guides make the X/Y/Z components of a straight-line measurement easier to visualize.

## Marker and line appearance

The Settings window includes controls for:

- marker size
- line width
- measurement-line visibility
- measurement-value visibility within world labels (the main window controls whether editor labels themselves are shown)
- measurement-value visibility within world labels (hide numeric dimensions while keeping names visible)
- X/Y/Z guide visibility
- selected-measurement emphasis
- selected marker and line multipliers
- inactive measurement opacity
- alternate GUI skin

The normal and Settings windows remember their positions between sessions.

## Undo

TapeMeasure has its own measurement-specific undo stack, separate from KSP's editor undo system. It keeps up to 50 measurement operations.

Undo covers measurement creation, point placement, symmetry-generated copies, deletion, Clear All, renaming, lock-mode changes, and endpoint dragging. A complete endpoint drag is recorded as one undo operation.

Use the **Undo** button or press **Ctrl+Z**.

The undo history is kept only for the current editor/craft session and is not written to the persistent measurement database.

## Clipboard and CSV export

Displayed measurement values can be copied to the clipboard. The Measurement List also provides:

- **Copy CSV** — copies the current measurement table as CSV text
- **Export CSV** — writes a timestamped UTF-8 CSV file under:

```text
saves/<SaveName>/TapeMeasure/Exports/
```

CSV output includes measurement type, name, lock mode, formatted value, raw distance/angle data, raw X/Y/Z meter components, and endpoint part names.

## Keyboard shortcuts

- **M** — toggle measurement mode
- **Esc** — exit measurement mode or endpoint-edit mode
- **Delete** — delete the selected measurement
- **Ctrl+C** — copy the selected measurement value
- **Ctrl+Z** — undo the most recent TapeMeasure measurement change
- **Shift** — temporarily enable configured snap targets while held

Command shortcuts are suppressed while typing in text-entry fields so normal text editing and clipboard operations continue to work.

## Persistence

Measurements are stored per KSP save under:

```text
saves/<SaveName>/TapeMeasure/Measurements.cfg
```

TapeMeasure stores part-local and vessel-local endpoint coordinates so measurements can be restored with their selected lock behavior.

Preferences are stored under:

```text
saves/<SaveName>/TapeMeasure/Settings.cfg
```

Saved preferences include window positions, snapping options, grid spacing, lock defaults, symmetry behavior, line/label/guide visibility, marker appearance, pane states, and other interface settings.

## Building from source

The Visual Studio solution is `TapeMeasure.sln`. The project targets .NET Framework 4.8 and expects `KSPDIR` to point to the KSP installation directory so it can reference KSP, Unity, ToolbarController, and ClickThroughBlocker assemblies.

A Release build uses the included deployment/release scripts to populate the GameData package.

## License

TapeMeasure is released under the MIT License. See [License.md](License.md).


While measurement mode is active, TapeMeasure hides the normal pointer and draws its custom measurement cursor directly, avoiding cursor flicker from KSP editor cursor changes.
