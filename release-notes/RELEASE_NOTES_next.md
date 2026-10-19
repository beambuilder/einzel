# Einzel v26.2.0 release notes

Draft. Append as things land; this file is renamed at release time, so a planned patch can
become a feature release without being renamed twice.

## What is new

- **`compact-astral-3d`**: the published Astral analyzer at one fifth of its size, as a whole
  three-dimensional instrument - both converging mirrors, the foil, the drift and its reversal.
  Every published length is kept beside one `scale` parameter that multiplies it, so it is
  exactly similar to `astral-3d` rather than approximately: its flight time is the full-size
  one times the scale to 1.6e-8. `planar-mirror-pair` and `astral-mirror` are now described as
  the cross-section studies they are.
- **A warning when a solve's mesh samples a face two conductors share** (`mesh.node-on-shared-face`).
  Conductors at different potentials may share a face, but if a mesh node, or the point where a
  stencil arm first meets metal, lies exactly on it, which potential it takes is decided by
  rounding - and no refinement study can see it, because every power-of-two mesh keeps the face
  on a node. The warning names the count, one example and the fix (move the solve domain a tenth
  of a cell), and reaches `einzel run`, `einzel preview`, every figure and `einzel solve`. No
  shipped template or example puts a node on a face two of its electrodes share.
- **The same warning when a conductor meets a grounded face of the domain.** A grounded face is a
  conductor at zero volts, and a node on it that lies on another conductor's surface holds that
  conductor's potential or zero according to rounding: the solve does not see it, but the field
  within about a cell of the contact moves by a fifth of the applied potential, and a conductor
  lying outside the domain against the face is in the solve only by the coin. **Seventeen shipped
  models now carry it** - ring stacks run out to the outer wall, the einzel lens, the Paul trap,
  the Kingdon trap and the two mirror cross-sections - and their figures are marked qualified.
  Fifteen are measured harmless: every figure is identical either side of the coin. In the two
  mirrors, `planar-mirror-pair` and `astral-mirror`, the end cap is a plate with no thickness
  lying in the grounded edge and is in the solve only because two expressions agree exactly; the
  shipped arithmetic lands on the side with a cap.
- **`einzel solve` reports warnings**, in `--json` and on stderr. It reported residuals and node
  counts and nothing else.
- **`einzel render still`** draws a model's shaded 3D view to a PNG, headlessly - the same
  picture the interactive viewport shows, from the same composition and camera. Named views
  (`--view iso|side|top|front`), pixel sizes (`--width-px`, `--height-px`), `--see-through`.
  The PNG carries the engine version, the model's hash and every warning in its text chunks,
  and a validity violation hatches the bottom of the picture.
- **`einzel export` carries a solve's warnings** into each field file's header and into its
  `--json` output, and prints them on stderr.
- **Volume solves hold far less memory, and smooth faster.** Where a conductor's surface
  crosses between two mesh nodes, the solve records how far along and at what potential - a
  cut link. These were stored for all six arms of every node, 96 bytes a node, although about
  4 percent of arms are cut; they are now stored for the cut arms alone, 1.5 bytes a node and
  16 a cut arm. A volume solve's peak falls from about 240 bytes a node to 150 on the Galerkin
  multigrid hierarchy and from 142 to 51 on the rediscretized one, so `astral-3d` at a 1 mm
  cell needs 9.5 GiB where it needed 15.3. The smoother runs 1.2 to 1.5 times faster on the three
  shipped volume templates timed, and every solve gives the same potentials to the last bit.
  `einzel estimate` bills a quarter of a cut arm a node, above every shipped template; a
  geometry with more cut arms than that is under-billed by 16 bytes for each one past it.
- **A scale in every 3D picture.** Stills and the cross-platform viewport carry a ruler in
  whichever corner the instrument covers least: one arm along each model axis, all the same
  round length (`10 mm`, `500 µm`),
  foreshortened exactly as the instrument is, so it stays true in the angled view; in a
  straight-on view it is an L-shaped scale bar. `--no-scale` leaves it out of a still, whose
  `--json` result reports the arm length and the corner. A picture too small to hold the ruler
  gets none.

## Fixed

- **Two unnamed electrodes in a driven or sequenced solve were solved at the same potential.**
  Every unnamed electrode was called `electrode`, and a driven or sequenced geometry is solved
  through channels keyed by name, so the later of two set both: a deflector of two unnamed
  plates at +2 V and -2 V was switched off, on a run that validated, converged and exited 0.
  The same happened to two electrodes given one name. An unnamed electrode is now called
  `electrode-N` after its place in the list, and **two electrodes in one solve sharing a name
  are refused** at validation, since a lost ion is reported by its electrode's name as well.
  Two field elements may still share names, as the Astral's mirror solves do for the same
  metal. A static geometry's field was right and is unchanged; only its unnamed electrodes'
  names in reports change. No shipped template or example is affected.
- **Two taps of one electrode on one generator kept only the later.** At one phase, or at two
  phases of a sinusoid, they reach the same supply, and 100 V and 50 V flew exactly as 50 V
  alone. They now add.
- **`astral-3d`'s flight time depended on rounding.** Three faces shared by foil slices at
  different voltages lay exactly on planes of mesh nodes, so which voltage the stencil arms in
  those planes took was decided by the last bit of arithmetic - worth 0.27 percent. The foil
  mesh now sits a tenth of a cell off every such face (`meshShiftZ`). The shipped flight time is
  784.90 us (was 786.44) and the drift reversal 336.06 mm (was 336.15). Moving the mesh also
  showed that the foil's coarse mesh carries a 0.83 percent spread by placement alone; the
  mesh-converged flight time is about 775 us, a percent below the published arithmetic rather
  than the 0.4 previously quoted.
- **Printed mirror boards are drawn in the viewport.** The planar mirror pair and the Astral
  mirror were drawn as their end caps alone, because a board declared on a domain edge has no
  interior for the surface extraction to find.
- **A reflected solve is drawn whole.** A mirror pair declared as one half and reflected was
  drawn with one mirror.
- **A zero-width electrode is lit.** Its surface had no normals, so neither viewport could shade it.
- **The cross-platform viewport fits the instrument to the frame.** A long, thin analyzer was
  drawn across the middle third of the picture.
- **`pnnl-ion-funnel` runs again.** Its density grid ended half a millimeter past its detector,
  which the diffusive mode refuses; the grid now ends at the detector by expression.
- **`einzel solve` printed a volume as a plane.** The terminal output gave only the first two
  node counts and spacings, so a 257x17x257 solve read as 257x17 with no z spacing. It now
  prints every axis. `--json` was always correct.
- **`einzel solve` printed a driven element's channels as identical blocks.** A driven
  structure is one solve per basis channel, and each was labeled only `field 0`, so the RF
  funnel's two channels looked like one solve printed twice. Each is now labeled
  `field 0 channel 0`, `field 0 channel 1`, matching the `channel` field in `--json`.
- **`einzel estimate`'s memory figure was a fifth of the truth.** It billed every solve 48
  bytes a node, where a volume solve held about 240 on the Galerkin multigrid hierarchy, 142 on
  the rediscretized one and 143 on a plane, measured against real builds: `astral-3d` at a 1 mm
  cell was quoted 3.1 GiB and needed 15.3. The volume figures are now about 150 and 51, and
  that `astral-3d` 9.5 GiB, since the cut links are stored for the cut arms alone (above). The
  estimate now counts basis channels, charges a
  diffusive mixture once per species, adds what earlier elements keep, says what its figure
  counted, and says so when the peak is more than half of what the process may use on this
  machine - whether or not anything in the model is solved.
- **A driven solve held finished channels' geometry** while solving the next: 353.7 bytes a
  node for two volume channels where 248.7 is what they need, and the driven plane build kept
  its first channel's mask, 73 bytes a node, through every later channel.
- **Building a volume solve's conductor mask allocated per stencil arm** on revolved and
  extruded electrodes: 5.5 kB a node on `c-trap`, 260 bytes on `linear-ion-trap-3d`, against
  the 105 the mask keeps. It now allocates only what it keeps, and the masks are bit-identical.
  `c-trap`'s build at 0.5 mm takes 14 s where it took 16; `linear-ion-trap-3d`'s is unchanged.
- **`einzel export` wrote an RF cross-section as a grounded box.** A two-dimensional geometry
  whose electrodes hold their potential as drive rather than DC exported every node at zero -
  `quadrupole-rf`, `linear-ion-trap`, `stellar-ion-trap`, `multipole-guide`, `paul-trap` and
  `travelling-wave-guide` - and one with a DC chain as well exported the chain alone, dropping
  the RF (`ion-funnel`, `pnnl-ion-funnel`, `tims-front-end`, `tims-tandem`). It now writes one file per basis channel, as
  three-dimensional export already did, named `.channel0.vti`, `.channel1.vti` where there is
  more than one. A driven channel is a pattern with its leading electrode at 1 V rather than a
  potential, so its array is named `channelN_V_per_V` and its header says which electrodes carry
  it; a static geometry's values are unchanged, and every file now carries its provenance and
  warnings in a header.
- **`einzel solve` reported the planar mirror pair without its boards.** A static geometry's
  edge-profile electrodes were solved at zero; the peak it printed was the cap's, so it looked
  right. Runs were never affected.
- **Extensions could not start on a Windows machine whose Python is 3.10 or older.** The
  sandbox started the interpreter with no environment at all, and CPython up to 3.10 reaches
  the random source it seeds hash randomisation from through CryptoAPI, which fails without
  `SYSTEMROOT`: every extension died with "Fatal Python error: _Py_HashRandomization_Init"
  before its first line, and `einzel ext test` failed on every project. The child now keeps
  `SYSTEMROOT` on Windows - the path of the Windows directory, and nothing else - and nothing
  anywhere else. CI never saw it because the Windows runner's Python is 3.12, which uses
  `BCryptGenRandom` and starts with an empty environment.

## Known limits

Carried forward from v26.1.0 until they change:

- **The Windows shell is partial**: seven of the eleven views in the specification exist.
- **A still cannot check the window's own GPU path**, and it does not draw the name of a
  validity violation as text: the codes are in the file's text chunks and on stderr.
- **No installer and no updater.** The assets are portable: unpack and run.
- **The extension sandbox states what it does not enforce** — no network confinement, no
  filesystem confinement, no memory ceiling — on every listing and on every result.
