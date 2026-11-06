---
name: ShopMind
description: An electronics store that reads like its own inventory — categories, stock, and prices encoded as resistor color bands.
colors:
  paper: "#f6f2e7"
  card: "#fffdf7"
  kraft: "#e4ddcc"
  kraft-deep: "#d6cdb6"
  ink: "#1a1a1a"
  ink-soft: "#514c41"
  line: "#6b6250"
  drawer: "#1a1a1a"
  drawer-ink: "#f0ece2"
  band-black: "#1a1a1a"
  band-brown: "#7a4a1e"
  band-red: "#d9342b"
  band-orange: "#c8631f"
  band-yellow: "#e0a800"
  band-green: "#2f7d3b"
  band-blue: "#2b5aa0"
  band-violet: "#6b3fa0"
  band-grey: "#83878d"
  band-white: "#fdfcf8"
  band-gold: "#c8a24a"
  band-silver: "#b8bdc4"
typography:
  display:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 800
    letterSpacing: "0.04em"
  wordmark:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "20px"
    fontWeight: 800
    letterSpacing: "0.3em"
  title:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "17px"
    fontWeight: 800
    letterSpacing: "0.03em"
  body:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "11px"
    fontWeight: 700
    letterSpacing: "0.16em"
  mono:
    fontFamily: "JetBrains Mono, monospace"
    fontSize: "13px"
    fontWeight: 400
  price:
    fontFamily: "JetBrains Mono, monospace"
    fontSize: "19px"
    fontWeight: 700
    lineHeight: 1
rounded:
  chip: "3px"
  control: "4px"
  cell: "6px"
  label: "8px"
spacing:
  sm: "8px"
  md: "16px"
  lg: "24px"
  xl: "32px"
components:
  button-add:
    backgroundColor: "{colors.band-red}"
    textColor: "{colors.card}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  button-ghost:
    backgroundColor: "{colors.card}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  button-human:
    backgroundColor: "{colors.band-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  band-chip:
    backgroundColor: "{colors.band-brown}"
    rounded: "{rounded.chip}"
    size: "22px 12px"
  card-product:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.label}"
    padding: "20px"
  field-input:
    backgroundColor: "{colors.card}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
  drawer-assistant:
    backgroundColor: "{colors.drawer}"
    textColor: "{colors.drawer-ink}"
    rounded: "{rounded.label}"
    padding: "20px"
---

# Design System: ShopMind

## Overview

**Creative North Star: "The Resistor-Code Bin Wall"**

ShopMind is a working electronics shop that reads like its own inventory. The palette is not a mood board — it is the IEERC ten-band resistor ladder, the same color code every engineer decodes on a component's body. Categories, stock levels, and price digits arrive as band chips on paper label cards, filed in kraft bins, inked in near-black. Depth and hierarchy come from tonal grounds (paper, card, kraft, ink) and hairline-precise borders, never from gradients or shadows. The single most load-bearing fact of the system: exactly one color is not data. Yellow, in filled-control form, is the human-action marker — the visual proof of the Assist invariant that the AI quotes and only a person pays.

The type pairing reinforces the fiction: Archivo, a sturdy grotesque worn in tight uppercase caps with wide tracking, does the shelf-label voice; JetBrains Mono, tabular, does every number — prices, quantities, SKUs, bin codes. Numbers in this world are always mono and always `tabular-nums`; a price that jitters is a price that lies. Where the direction contract proposed slightly different grounds (`#F8F5EC` paper, `#141210` dark base), the build shipped `#f6f2e7` and `#17140e`; the built values are normative.

**Key Characteristics:**
- The resistor color ladder *is* the palette; every band carries real data (category, stock, or a price digit).
- Grounds are packaging material — paper, kraft cardstock, ink drawer — not accents.
- One non-data color: yellow as a filled control means "human only."
- Flat by construction: 1.5px solid outlines and tonal steps convey all depth; zero shadows, zero gradients.
- Bands never decode alone: the digits are always printed beside them.
- Dark mode inverts the grounds warm; band colors keep their exact identity.

## Colors

The palette is two families: packaging grounds that flex between themes, and the band ladder that never does. Text placed on a band always uses ink — never white-on-yellow.

### Primary
- **Resistor Red** (`band-red`): The add control ("Add to bin"), error borders and error dots, and the text caret in every input. Red is action-and-fault, the shop's "grab / wrong" signal.

### Secondary
- **Human Yellow** (`band-yellow`): The system's only non-data color when it appears as a filled control — pay/bill review, the cart chip once it holds items, the text-selection highlight. The same hue also appears as *data* in miniature (low-stock chip/dot, the gold tolerance band); form disambiguates: yellow as a filled control with ink text is always a human action; yellow as a small chip or dot is always stock data. The assistant surfaces yellow links for humans but never a control it can press itself.

### Tertiary — the encoding ladder
- **Band Brown / Orange / Green / Blue / Violet**: the five fixed category identities (laptops/phones/audio/displays/components). A band is never reused for two categories. Green additionally means "in bin" in the stock trio.
- **Band Black / Grey / White** plus brown→violet: the digit ladder 0–9 (`black`=0 … `white`=9); **Gold / Silver** are tolerance metals with no digit value. These appear in price strips, the masthead's five-chip category sequence, legends, and the ten-rung value ladder.

### Neutral
- **Paper** (`paper`): page ground — warm packing paper.
- **Card** (`card`): label-card and input ground — a shade whiter than paper.
- **Kraft / Kraft Deep** (`kraft`, `kraft-deep`): bin rail, masthead, table-key cells, skeleton fill — cardboard tones.
- **Ink** (`ink`): text, borders, the filled nav-active chip.
- **Ink Soft** (`ink-soft`): secondary text, meta lines, mono captions.
- **Line** (`line`): the quiet 1.5px border and dashed-divider color.
- **Drawer / Drawer Ink** (`drawer`, `drawer-ink`): the assistant's black-drawer card and its off-white text; the drawer pair is theme-stable in light mode and deepens (`#0d0b07`) in dark.

### Named Rules
**The Ten-Band Rule.** The ladder is the entire accent palette. No new accent colors enter the system; anything that encodes data picks a band, and anything that isn't a band is a ground.
**The Yellow-Is-Human Rule.** Yellow in filled-control form marks actions only a human may take. The assistant's surface may *show* a yellow link ("Review bill") but no yellow control may be one the agent can press, and no pay action renders inside chat. The bin-rail legend states this in words; the system states it in color.
**The Ink-On-Band Rule.** Text on any band uses ink (`#1a1a1a`), with the single exception of category corner tabs, which use card-white on saturated bands (and flip back to ink on `band-white`). Never white-on-yellow.

## Typography

**Display Font:** Archivo (system-ui fallback), weights 400–800 via `next/font`
**Body Font:** Archivo, 400
**Label/Mono Font:** JetBrains Mono, weights 400 and 700

**Character:** Archivo in tight uppercase caps with heavy tracking reads like stenciled bin labels — industrial, patient, legible at 10px. JetBrains Mono is the ledger voice: it carries every number and every machine trace, and it never sets prose.

### Hierarchy
- **Display** (800, 24px, uppercase, tracking 0.04em): page headlines ("Bin wall — every bin"). Count-suffix rides inline in mono 14px normal-case `ink-soft`.
- **Wordmark** (800, 20px, uppercase, tracking 0.3em): the "ShopMind" masthead mark; its catalog-code tail is mono 10px tracking 0.24em.
- **Title** (800, 17px, uppercase, tracking 0.03em): product names on label cards; section titles in the UI kit run Archivo 800 18px tracking 0.08em.
- **Body** (400, 14px, line-height ~1.6): prose, assistant dialogue, empty-state copy. Max width ~62–65ch where paragraphs run long.
- **Label** (700–800, 10–12px, uppercase, tracking 0.08em–0.26em): nav items, field labels, rail headings ("Bins", "Stock dots", "Value ladder"), buttons (12px, tracking 0.14em). Tracking widens as type shrinks.
- **Mono/Data** (JetBrains Mono 400, 10–13px): SKUs, bin counts, model lines, search input, tool-trail, plate captions.
- **Price Figure** (JetBrains Mono 700, 19px, line-height 1, `tabular-nums`): the printed number beneath every price band strip; smaller money (cart total, UI-kit figures) uses mono 13–14px 400–700 `tabular-nums`.

### Named Rules
**The Digits-Beside-Bands Rule.** Every band encoding prints its digits or a text label alongside the chips — prices show the mono figure under the strip, stock shows a word ("in bin · 7"), the legend spells the code out. A band alone is never the only carrier of a value (colorblind fallback, fixed).
**The Tabular Rule.** All money, quantities, and counts render in JetBrains Mono with `tabular-nums`. Prose never sets numbers in Archivo.

## Layout

The shell is a sticky masthead over a two-column shop: a fixed-width left bin rail (248px at `lg`, collapsing to a horizontal chip row plus a `<details>` legend below it) and a flexible main column padded 24px. Everything centers in a 1600px max-width container.

- **Catalog grid:** 1 column at base, 2 at `md` (768px), 3 at `2xl` (1536px), 24px gutters. Cards are equal-width paper labels.
- **Assistant column:** at `xl` (1280px) the assistant drawer docks as a sticky 400px right column inside the first viewport, offset below the masthead; below `xl` it flows at the end of the page. A floating minimized variant (bottom-right, 420px) exists for tool surfaces.
- **Masthead rows:** wordmark + category chips + primary nav + search (grows to 380px max) + cart + theme toggle at `lg`; below `lg` the nav becomes a bordered chip row that scrolls horizontally.
- **Density:** the system runs tight — 20px internal card padding, 16px between card blocks, 6–8px between chips and dots. Section breaks are dashed 1.5px rules with 32px vertical rhythm (UI kit: `py-8`).
- **Page height:** the shop layout fills `calc(100vh - 57px)` minimum so the kraft rail's border reaches the fold.

## Elevation & Depth

Flat by construction. There are no box-shadows and no gradients anywhere in the built system. Depth is entirely tonal: paper < card < kraft < kraft-deep < ink/drawer, with a 1.5px border always marking where one material ends and another begins. The drawer card inverts to black to read as a separate object pulled from a tool-case, not as a lifted surface.

### Named Rules
**The Flat-Outline Rule.** Surfaces stack by material color and border weight only. If a screen needs to feel "above" another, it changes ground (card on paper, kraft on card) or border color (line → ink), never elevation. Hover deepens borders and brightens fills (`brightness`), it does not lift.

## Shapes

- **Border:** 1.5px is the one true stroke weight throughout — solid for objects, dashed for frames, dividers, and placeholder states. The registration-corner motif (four 12px dashed L-crops) marks a photo plate that's awaiting its image.
- **Radius ladder, from data to prose:** 2px (focus ring) < 3px `chip` (band chips, dots, corner tabs, figcaptions) < 4px `control` (buttons, inputs, skeletons, ladder strip) < 6px `cell` (bin-rail rows, legend summary) < 8px `label` (`--radius-label`: product cards, drawer cards, empty states). Small things are sharper; cards read as die-cut labels.
- **Silhouette:** rectangles standing on edges — chips are vertical slivers (taller than wide, like bands on a resistor body), dots are true circles, plates are 150px slots. The whole UI has the geometry of a printed catalog sheet.

## Components

### Buttons
**Shape:** crisp 4px radius, 1.5px border, 12px extrabold uppercase, tracking 0.14em, padding 8px 16px, icon slot at 13px.
- **Add (primary action):** resistor-red fill, card-white text, red border; hover brightens ~10%; disabled drops to 40% opacity with pointer events off. This is the "grab this part" control — never rendered inside the assistant's own action set as a pay control.
- **Ghost:** card fill, ink text, line border; hover shifts border to ink and fills kraft. Carries secondary actions (remove-from-bin, clear filter, disabled "Bin empty").
- **Human (yellow):** resistor-yellow fill, ink text, ink border; hover brightens ~8%. Reserved exclusively for actions that cross the money boundary — "Review bill & pay." It must never be used for assistant-side or purely navigational emphasis.
- **Focus (all):** 2px dashed ink outline, 3px offset, 2px radius — browser-wide via `*:focus-visible`.

### Band Chip & Band Strip (signature)
Vertical color slivers with a 1.5px ink border (dark theme: line color), 3px radius, in three sizes: 12×7px (inline/meta), 22×12px (default), 32×16px (legend). A strip lays chips end-to-end at 3px gaps, bottom-aligned. On mount, a strip **decodes left-to-right**: each chip fades and rises 7px on a 0.55s `cubic-bezier(0.16, 1, 0.3, 1)` ease with a 70ms per-chip stagger, `backwards` fill so content is visible once the pass completes. Under `prefers-reduced-motion`, animations collapse to 0.01ms — the decode never becomes a barrier.

### Stock Dots
Three 9px circles, 4px gaps, 1.5px ink border. Filled count reads as level: 3 full = "in bin" (green), 2 full = "low stock" (yellow), 1 full = "bin empty" (red); unfilled dots are transparent at 30% opacity. Always followed by the mono label `STOCK · qty`.

### Photo Plate
150px slot, 1.5px border. **Empty state (honest):** dashed line border on kraft at 50%, registration corners, a small category chip and mono caption "PL. {sku} · photo not on file." **Filled state:** solid border, image contained with 8px inset, figcaption chip-over-card at 90% opacity with plate number. Photos ship with local provenance files; an absent photo is a stated condition, not a lie.

### Product Label Card
8px radius, 1.5px ink border, card fill, 20px padding. A category tab straddles the top-right edge (−9px): band-filled, ink-bordered, card-white mono text, `CAT · SKU`. Inside: plate slot, extrabold name, mono model line, a kraft-key spec table with 1px line rows, then the money row — band strip over its printed mono price on the left, stock dots on the right, controls below.

### Field / Input
4px radius, 1.5px line border, card fill, 14px text, 8px 12px padding; floating 11px uppercase tracking-0.22em label above. Focus: border deepens to ink (plus the global dashed ring). Error: border and message turn resistor-red, message `role="alert"` at 12px semibold. Disabled: 40% opacity. Red caret browser-wide.

### Assistant Drawer (signature)
Black drawer card (8px radius, 1.5px ink border, drawer fill, drawer-ink text, 20px padding). Header line "The assistant · turn {n}" in 13px extrabold uppercase tracking 0.26em with icon. Dialogue quotes inline band chips (sm) with the digit string printed beside them; a mono 11px tool-trail line lists every call that produced the numbers. Actions row: ghost-style "add" button on drawer borders, a yellow human-only "Review bill" link, and a standing mono footnote: payment is on the bill page · a yellow control is human-only · the assistant cannot press it. Collapsed variant renders as a fixed bottom-right pill.

### Rail Legend
Dashed-topped block in the bin rail with the stock-dot trio, the ten-rung value ladder (16px strip, full-width segments, `0 = black` tooltips), the worked decode example ("brown·black·white·white = 1·0·9·9"), and the written Yellow-Is-Human clause. The legend is not decoration — it is the system's self-teaching surface and ships on every catalog view.

### Loading / Empty / Error states
Skeleton: 4px radius, line border at 40%, kraft-deep fill at 40%, pulse. EmptyState: 8px radius, dashed border, card fill, 32px padding, extrabold uppercase title. ErrorNote: 4px radius, solid red border, red dot marker, `role="alert"`. All three are demonstrated live at `/dev/ui`, the system's state gallery.

### Masthead & Nav
Kraft bar, 1.5px ink bottom border. Active nav item inverts to ink fill with paper text; hover tints ink at 10%. Cart chip is line-bordered `ink-soft` when empty and flips to yellow fill with extrabold ink mono when items are present — the cart turning yellow is the human's basket gaining weight, never the assistant's.

## Do's and Don'ts

### Do:
- **Do** encode data only with ladder bands, and print the digits beside every encoding (price strip + mono figure; stock dots + "in bin · 7").
- **Do** keep all 1.5px borders — solid for real edges, dashed for frames, dividers, and awaiting-content states.
- **Do** reserve yellow filled controls for actions a human must take across the money boundary; surface them in assistant contexts only as links *to* human-only pages.
- **Do** set every number in JetBrains Mono with `tabular-nums`, and every label in uppercase Archivo with tracking widened as size drops.
- **Do** let band colors ride both themes unchanged; only grounds (`paper`, `card`, `kraft`, `ink`, `line`, `drawer`) invert, and the dark inversion stays warm (`#17140e` ground, `#f0ece2` ink), never blue-grey.
- **Do** use the band-set decode (0.55s, 70ms stagger) only for price strips entering the view; everything else at rest is static.
- **Do** theme the browser surface with the world: dashed ink focus ring (2px, 3px offset), yellow selection on ink text, red caret, kraft scrollbar track with a brown (light) or line (dark) thumb.
- **Do** ship loading, empty, error, and partial states on every surface, and let the empty photo plate say "photo not on file."

### Don't:
- **Don't** put a pay or charge control inside the assistant panel or chat — the capability does not exist in the agent's tool surface and the UI must not fake it.
- **Don't** reuse one band for two categories, or introduce an accent color outside the ladder and the grounds.
- **Don't** use shadows, gradients, or glass effects; depth is tonal.
- **Don't** render white text on yellow, or any band text that isn't ink (the card-white category-tab exception aside).
- **Don't** show a band encoding without its printed digits — the color carries flavor, never sole meaning.
- **Don't** rename "ShopMind" or spell the world's categories in lowercase; the shelf-label register is fixed.
