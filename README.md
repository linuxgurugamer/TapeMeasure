# TapeMeasure

TapeMeasure is an editor-only measuring and layout tool for **Kerbal Space Program 1**. It runs in the VAB and SPH and lets you place, organize, edit, display, and export measurements directly on a vessel.

It is useful for checking vehicle width and height, wheelbase, engine spacing, payload clearances, wing geometry, landing-gear layout, attachment locations, structural angles, and other editor geometry.

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

TapeMeasure starts with its main window hidden. Enter the VAB or SPH and click the TapeMeasure toolbar icon to open it. The toolbar icon changes while measurement mode is active. If the option to hide the main window while measuring is enabled, TapeMeasure shows a small **Press Esc to end measuring** reminder while the window is hidden; the reminder blinks on and off every two seconds.

The main window contains collapsible panes for snapping, automatic vessel dimensions, the measurement list, and the selected measurement. Pane headings remain visible when collapsed and the window resizes to fit the visible content. When the Measurement List is expanded, TapeMeasure also measures the displayed data and automatically widens the window as needed so the row values remain on one line.

An optional setting can temporarily hide the main window while a measurement is being placed. It returns automatically when measurement mode ends.


## Settings window

The Settings window always starts closed when entering the editor; its open/closed state is not saved between sessions. Settings are organized into five tabs:

The Settings window uses a compact, scrollable height.

- **Snapping** — snap targets, symmetry-aware measurements, and the default measurement lock mode.
- **Interface** — skin selection, pane expansion defaults, and hiding the main window while measuring.
- **Display** — units, precision, measurement lines, labels, guides, angle arcs, end ticks, and vessel bounding-box display.
- **Markers** — marker size, line width, selected-measurement emphasis, and inactive opacity.
- **Keyboard** — configurable key bindings and reset-to-default controls.

## Interrupted measurements

Settings → Interface includes **Remember unfinished points when Start Measuring is clicked again; when an unfinished measurement can be resumed, the main button changes to **Continue Measuring****. When enabled, stopping measurement mode after placing point A (or A/B of an angle) and later pressing **Start Measuring** resumes from those points. When disabled, **Start Measuring** discards the unfinished points and starts a fresh measurement of the same type.

## Measuring a distance

1. Click **New Distance**, or use the configured measurement shortcut.
2. Click point **A** on the vessel.
3. Move the mouse. A live preview line follows the mouse or current snap target.
4. Click point **B**.
5. The completed measurement is added to the Measurement List.

TapeMeasure displays the straight-line distance and the vessel-axis X, Y, and Z components. Distance lines can also show CAD-style end ticks.

## Measuring an angle

1. Click **New Angle** or use its configured shortcut.
2. Select point **A**.
3. Select point **B**, which is the angle vertex.
4. Select point **C**.

The resulting A-B-C angle is displayed in degrees. Optional angle arcs are drawn around vertex B.

## Axis constraints while placing points

The next point can be constrained to a vessel axis through the previous point. The defaults are:

- **Alt+X** — constrain along vessel X
- **Alt+Y** — constrain along vessel Y
- **Alt+Z** — constrain along vessel Z

These bindings can be changed in Settings. Axis constraints work with the live preview and can be combined with snapping.

## Measurement list

The Measurement List supports large sets of measurements without changing their stored creation order.

You can:

- filter by measurement name, group, or type
- sort by creation order, name, type, or value
- reverse the selected sort order
- select a measurement by clicking its row
- show or hide individual measurements
- delete a measurement with the red `X`
- click displayed values to copy them
- change a measurement color using its color swatch

Hovering a list row temporarily emphasizes that measurement in the editor without selecting it. Hovering a visible measurement line or endpoint in the editor highlights its row in the list.

## Measurement groups and categories

Each measurement can be assigned to a named group, such as:

- Landing Gear
- Payload
- Engines
- Wings

Use the **Group** control in the Selected Measurement pane to assign or clear a group.

The Measurement List displays each group under a collapsible heading. The visibility checkbox on a group heading shows or hides all measurements in that group. Individual measurement visibility is still retained separately and participates in Undo/Redo.

Measurements without a group appear under **Ungrouped**.

## Per-measurement colors

Each measurement has its own persistent line and marker color. New measurements automatically rotate through a palette so adjacent measurements are easier to distinguish.

You can change a color by:

- clicking the color swatch in the Measurement List to cycle through the palette, or
- selecting a measurement and choosing a color in the Selected Measurement pane.

The selected color is used for its line, markers, angle arc, and distance end ticks.

## Selection and endpoint editing

Creating a measurement does not automatically select it. Click a row in the Measurement List to select and highlight it.

The Selected Measurement pane lets you:

- rename the measurement
- assign a group
- choose its color
- show or hide it
- switch between Part-relative and Vessel-relative locking
- edit its endpoints
- inspect and copy its values

Use **Clear Selection** to remove the highlight without deleting the measurement.

When endpoint editing is enabled, drag A/B for a distance or A/B/C for an angle. Snapping remains available while dragging.

## Snapping

Snapping has a master switch plus individually selectable target types:

- Part origin
- Attachment node
- Surface attachment point
- Part center
- Vessel root
- Center of Mass
- Center of Lift
- Center of Thrust
- Vessel Axis / Grid
- Existing TapeMeasure endpoints

Multiple target types may be enabled at once. TapeMeasure selects the closest valid target within the configured snap radius.

The configured snap modifier temporarily activates snapping without changing the saved master setting. By default this is **Shift**.

Existing TapeMeasure endpoint snapping uses visible A/B/C points from other measurements and excludes the measurement currently being created or edited.

## Part-relative and vessel-relative measurements

Each measurement has a lock mode:

- **Part-relative** — each point remains attached to the Part on which it was placed.
- **Vessel-relative** — the point remains fixed relative to the vessel reference frame even if the original Part is moved.

Switching modes rebases the saved coordinates at the current visible position, so the measurement does not jump when the mode changes.

## Symmetry-aware measurements

When enabled, completing a measurement on parts with KSP symmetry counterparts automatically creates corresponding measurements on those counterparts. Generated copies inherit the source measurement's group, color, visibility, and lock mode.

## Automatic vessel dimensions

The Automatic Vessel Dimensions pane calculates the vessel's visible render envelope and shows:

- Length
- Width
- Height
- X × Y × Z bounding-box dimensions

The dimensions refresh automatically on `onEditorShipModified`. An optional editor visualization draws the vessel bounding box.

## Guides, arcs, and dimension graphics

Display options include:

- X/Y/Z projection guides for the selected distance measurement
- angle arcs for completed angle measurements
- CAD-style end ticks on distance lines
- vessel bounding-box visualization
- measurement labels and optional values
- marker size and line width controls
- selected-measurement emphasis and inactive opacity

Guide colors remain X = red, Y = green, and Z = blue.

## Undo and Redo

TapeMeasure has its own measurement-only Undo/Redo history and does not use KSP's vessel editor history.

Undo/Redo covers measurement creation and deletion, endpoint edits, names, groups, colors, visibility, lock changes, symmetry-generated copies, and other TapeMeasure data changes.

The history is capped at 50 operations. A new change after Undo clears the Redo branch.

## Customizable keyboard shortcuts

Open Settings to configure the keyboard bindings. Click a binding and press the replacement key combination. Bindings may also be cleared or reset to defaults.

The configurable actions include:

- measurement mode
- cancel mode
- new distance
- new angle
- endpoint editing
- show/hide labels
- delete selected
- copy selected
- Undo
- Redo and alternate Redo
- temporary snapping
- vessel X/Y/Z constraints

Default core bindings remain compatible with earlier TapeMeasure releases: `M`, `Esc`, `Delete`, `Ctrl+C`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+Shift+Z`, and `Shift` for temporary snapping.

## Clipboard and CSV export

Displayed values can be clicked to copy them to the clipboard. The measurement list can also be copied or exported as CSV.

CSV export includes type, name, group, color, visibility, lock mode, formatted value, raw distance/angle values, vessel-axis components, and endpoint Part names.

Exports are written beneath the current KSP save in:

```text
saves/<SaveName>/TapeMeasure/Exports/
```

## Persistence

Completed measurements are stored per save in:

```text
saves/<SaveName>/TapeMeasure/Measurements.cfg
```

The current database format is version 6. Older TapeMeasure measurement files remain loadable; measurements without saved group/color data are placed in **Ungrouped** and use the default measurement color.

Interface and appearance preferences are stored separately in:

```text
saves/<SaveName>/TapeMeasure/Settings.cfg
```

## Building from source

The project targets .NET Framework 4.8 and expects `KSPDIR` to point at the KSP installation. Open `TapeMeasure.sln` in Visual Studio and build the project, or use the included build scripts.

## License

See `License.md`.

## Guide value labels

When X/Y/Z projection guides are enabled for a selected distance measurement, Settings can also show the formatted X, Y, and Z values directly on those guide lines. Guide value labels follow the current distance-unit setting and are suppressed when measurement dimension values are disabled.


## Display precision

Settings includes a **Precision** control with Automatic, 1, 2, 3, and 4 decimal-place modes. Automatic preserves TapeMeasure's unit-aware formatting. Fixed precision applies to displayed distances, angles, editor labels, guide labels, clipboard values, and the formatted Value field in CSV exports. Raw stored measurement values and raw CSV numeric columns are unchanged.

## Measurement notes

Each measurement has an optional multi-line **Notes** field in the Selected Measurement pane. Notes are saved with the craft measurement, preserved by Undo/Redo and symmetry copies, searchable through the measurement-list filter, and included in CSV exports.

When measurement mode starts, the Settings window closes automatically. **Esc** always exits measurement or endpoint-edit mode, regardless of the configurable Cancel binding.


## Cursor state indicators

While measurement mode is active, the cursor changes to show the current placement state. A green snap-target badge indicates that snapping is active. Holding a vessel-axis constraint adds **X**, **Y**, or **Z** to the cursor.

The hidden-window **Press Esc to end measuring** reminder flashes once per second.


### Settings tabs

Settings are organized into **Snapping**, **Interface**, **Display**, **Markers**, and **Keyboard** tabs. The Keyboard tab uses a scrollable bindings list that fills the available Settings-window content area.
