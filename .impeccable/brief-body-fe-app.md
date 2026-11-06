# Surface brief — ShopMind app shell + design system (fe F0)

Mode: operate. Scope: the whole Next.js app world (shell, tokens, primitives) established at F0 and inherited by all later surfaces.

## Direction contract

THESIS: The store speaks in resistor color codes — categories, stock, and budget read as band chips every engineer can decode, replacing the generic one-accent commerce look; a legend always stands beside the code so newcomers learn it in one glance.

OWN-WORLD: IEERC ten-band ladder (black, brown, red, orange, yellow, green, blue, violet, grey, white) as the entire palette; paper tape #F8F5EC / kraft bin rail #E4DDCC / ink #1A1A1A backgrounds; no gradients, no shadows beyond flat outlines; Archivo-style sturdy grotesque in tight caps, tabular mono figures; 1.5px solid ink borders, small radii only on label cards.

STORY: The reviewer sees a working electronics shop that reads like its own inventory; the demo shopper decodes stock/category at a glance via bins and bands; the assistant's every number arrives as a band chip traceable to a tool call; the one non-band color in the system is the human-action yellow.

FIRST VIEWPORT: Masthead rail (wordmark + five category band chips + cart); left bin rail with drawer cells and the two legends (category, stock bands) + ten-rung value ladder; catalog as paper label cards with corner category tabs, band-encoded price strip, stock-dot row, red ADD control; assistant panel as a black drawer card quoting "reads like this turn's search band sequence" with its own chips and one yellow human-only "pay yourself" action.

FORM: Resistor Code, grounded candidate #1, chosen by the user as IMPECCABLE'S PICK over roll-assigned Rating Plate. seed:ae69978771e5b0c4.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.

## Unresolved decisions

- Band encoding for price (digits→bands) needs a fallback for colorblind users: digits stay printed alongside bands everywhere.
- Dark mode: inverted kraft paper (#141210 ground, warm off-white ink) — bands keep identity; to validate at build.
