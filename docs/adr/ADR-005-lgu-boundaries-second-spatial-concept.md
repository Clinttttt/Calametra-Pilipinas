# ADR-005 — LGU boundaries are a second spatial concept, not a replacement for the radius

**Status** Accepted · 2026-09-15 · **Implementation gated** — see [What must be settled first](#what-must-be-settled-before-any-code)
**Relates to** `GeoNamesPlaceSource.UniqueDerivedCodes`, `GetPlaceContext`

## Context

Every spatial answer this platform gives about a place is measured from a **point**. The place
directory holds 1,750 rows — 17 regions, 86 provinces, 139 cities, 1,508 municipalities — as
representative coordinates, refined onto town centres from OpenStreetMap. No boundary is stored for
any of them, and the interface says so out loud in every place readout:

> Distances are measured from the mapped town centre of Surigao City, not from its boundary. No
> boundary is stored for Philippine local government units, so for a large municipality its edge may
> be tens of kilometres from this point.

That caveat is honest and it is also a standing invitation: a reader looking at the national map
reasonably expects to click land and be told which municipality they clicked, and to have the
archive answer for that municipality rather than for a circle around its poblacion.

Adding boundaries is therefore wanted. But the work is **not** a map interaction with a dataset
attached; it is an identity problem with a map interaction on the end of it. Two facts already
recorded by this project decide that:

**The Philippine place code has two editions and they do not convert.** GeoNames publishes the
pre-2019 nine-digit PSGC, which is what `places.psgc_code` currently holds. The PSA publishes a
ten-digit register today. For most provincial municipalities the digits can be re-sliced — Adams is
`012801000` and `0102801000` — but Metro Manila was recoded wholesale, where Quezon City is
`137404000` against `1381300000`. No code is derived anywhere in this system for that reason: a
computed crosswalk would be right in most of the country and confidently wrong in the capital.

**Names do not identify LGUs.** 111 city and municipality names in our own directory are not
unique. Matching a boundary set to our rows by name is therefore wrong in at least 111 places before
anyone counts the diacritics, the `City of` prefixes and the renamings.

## Decision

### D1 — Containment and proximity are two concepts. Neither replaces the other.

An LGU boundary answers **attribution**: which unit is this event in, and what does this unit's
record hold. A radius answers **comparison**: how much of the archive lies within the same distance
of two different places.

They are not substitutes, and the reason is arithmetic rather than taste. Philippine LGUs differ in
area by more than two orders of magnitude. "Earthquakes within Cantilan" against "earthquakes within
Quezon City" compares a 218 km² municipality with a 161 km² city and a 2,000 km² one somewhere else —
the count would then encode land area as much as seismicity, which is precisely the class of error
this platform exists to refuse. Equal-area comparison needs a circle. Administrative attribution
needs a polygon.

So `GetPlaceContext` keeps its radius exactly as it is, the Compare page keeps comparing at a shared
radius, and boundary containment arrives as **its own query with its own caveats**. Any figure
derived from a boundary must state the unit's area beside it, for the same reason every magnitude
states its scale.

### D2 — Geometry comes from OpenStreetMap administrative relations, under ODbL 1.0

The PSA publishes the PSGC register but not open geometry. The practical open source is OSM, which
this platform already reads for town centres, so the licence position is one we have already taken
rather than a new one.

**Verified against the OSM Philippines mapping conventions, 2026-09-15** — and the levels are not
the ones a general-purpose guide would guess:

| Our `kind` | OSM `admin_level` |
|---|---|
| Region | **3** |
| Province | **4** |
| City / Municipality | **6** |
| *(not held)* Barangay | 10 |

Cities and municipalities are level **6** in the Philippines, not 8; level 8 is "other
administrative districts". An import written against the common assumption would silently fetch the
wrong tier.

**ODbL is share-alike, and that has to be stated where it applies.** The town-centre coordinates
already put the place directory under ODbL; adding boundaries deepens that rather than changing it.
Any derived place database we publish inherits the obligation, and the Sources page must carry it as
a stored source with its own row, not as a footnote.

### D3 — Canonical identity is the current PSA ten-digit PSGC. The nine-digit code becomes an alias.

A new `lgu` identity is introduced whose primary key is the **PSA ten-digit PSGC**, because that is
the register a Philippine institution will hand us and the one a professional will cite. The
existing nine-digit `places.psgc_code` is retained as a **historical alias**, not migrated over and
not reinterpreted.

An LGU therefore carries: the canonical ten-digit code, zero or more historical codes, the
authoritative name from the register, and a link to the directory row that supplies its point.

### D4 — Matching may **propose** a pairing. Only reviewed evidence may **establish** one.

Algorithmic matching is allowed, and refusing it outright would be theatre: 1,750 rows is too many to
pair by hand from nothing, and digit re-slicing plus name normalisation will correctly suggest most of
the provincial municipalities in one pass. What is forbidden is letting that pass *be* the crosswalk.

So the crosswalk has two states and they are not interchangeable:

| State | Who writes it | May a query read it? |
|---|---|---|
| `Proposed` | a matcher, in bulk | **No.** Never. |
| `Confirmed` | a person, with evidence recorded | Yes |

**A proposal is a work queue, not data.** `Proposed` rows exist so a reviewer has somewhere to start
and so the unmatched remainder is visible. An unreviewed pairing behaves exactly as if it were absent,
which is the same treatment this platform gives an unmeasured depth: not approximated, marked.

**"Unreadable" is a runtime boundary, not a hope.** The rule that no figure may be derived from a
`Proposed` row cannot be expressed as a table constraint — a `CHECK` constrains the row it sits on, it
cannot forbid a `SELECT`. So the boundary is enforced where reads actually happen, in three layers
that fail independently:

| Layer | Enforces | If it is bypassed |
|---|---|---|
| A `Confirmed`-only view, and a repository that exposes no other read path | Analytics can only see confirmed pairings | The next layer still holds |
| Database permissions on the base table, where the deployment allows separate roles | The application role cannot read `Proposed` at all | Falls back to the view boundary |
| Tests asserting no analytics query touches the base table, and that a `Proposed` row is invisible through every public read | Regression cannot reintroduce the join | — |

Permissions are qualified deliberately: a single-role local deployment cannot separate them, and an
ADR that mandates what the environment cannot supply is ignored rather than obeyed. Where roles are
available the base table is unreadable to the application; where they are not, the view and the tests
carry it, and that difference is recorded rather than glossed.

**Constraints enforce completeness, which is a different thing.** A row may not be `Confirmed`
without the evidence that makes it reviewable: `Evidence` present, `Reason` present where the evidence
is `ManualReview`, reviewer and date recorded, and the register edition cited. That *is* expressible
as a constraint, so it is one — a half-filled `Confirmed` row is the failure mode that would make the
whole gate decorative, because it would pass every read boundary while carrying no evidence at all.

**Confirming requires stating the evidence, per row.** The `Evidence` column carries what was
actually checked:

| Evidence | Means |
|---|---|
| `RegisterMatch` | The cited PSA publication states both codes for the same unit |
| `DigitReslice` | The nine-digit form is a documented re-slicing of the ten **and** the register's name agrees |
| `ManualReview` | A person decided from named sources, recorded in `Reason` |

`DigitReslice` is the one that needs guarding, because it is the rule a matcher can apply and it is
the rule that fails in the capital. It may only be confirmed where the register's own name agrees;
where the digits re-slice but the names do not, the row stays `Proposed` and goes to review. Quezon
City — `137404000` against `1381300000` — does not re-slice at all and must arrive as
`ManualReview` or not at all.

**No promotion in bulk.** A reviewer confirms rows individually or in a named, dated batch that
records which register edition and which sources were consulted. "Accept all proposals above
threshold N" is not review; it is the algorithm establishing the crosswalk with a person's name on
it.

**The matcher's accuracy is measured, not assumed.** Before the crosswalk is accepted, the
proportion of proposals a reviewer *rejected* is recorded in this ADR. If a matcher proposes 1,600
pairings and 40 are wrong, that number is the reason the review gate exists and it belongs in the
record.

**An unpaired unit is valid, not broken.** An LGU with no historical code is a valid LGU; a directory
row with no canonical code is a valid row. Neither is backfilled to make a join tidy, and neither is
hidden from the reader to make a count look complete.

### D5 — Ambiguity and exceptions are first-class, with a written reason each

The known population, all of it already documented in this codebase or its data:

| Case | Why it cannot be resolved mechanically |
|---|---|
| Metro Manila | Recoded wholesale in the ten-digit register; digits do not re-slice |
| Maguindanao del Norte / del Sur | Split in 2022; both halves still carry the undivided province's nine-digit code |
| Isabela City | Belongs to Basilan, administered under Region IX |
| Cotabato City | In BARMM since 2019, geographically within SOCCSKSARGEN |
| Negros Island Region | Created 2024; a region that did not exist when the nine-digit edition was published |
| 111 duplicate city/municipality names | Name is not an identifier |
| 5 directory rows with no code at all | Administrative history, not missing data |

Each exception is a row in the crosswalk with a `Reason` string that is **rendered to the reader**
where it affects a figure — the same treatment depth quality and magnitude scale already receive. A
caveat held only in a code comment is not a caveat.

### D6 — Delivery is zoom-banded; selection is independent of layer state

Drawing every city and municipality outline at national zoom — some sixteen hundred of them, and the
exact figure is whatever the active register says rather than a number recorded here — turns the
archipelago into a
spiderweb and buries the data. So:

| Zoom | Regions | Provinces | Cities / Municipalities |
|---|---|---|---|
| National | — | hairline | off |
| Regional | hairline | visible | faint |
| Local | — | visible | visible, hover enabled |

And the distinction that must be preserved in the implementation: **a layer being enabled means
"draw many boundaries"; a unit being selected means "always draw that one"**. A selected LGU's
outline appears whether or not the boundary layer is on, because it is then part of the answer rather
than part of the basemap.

Geometry is served simplified per band, not full-resolution at every zoom. A boundary drawn at
national zoom carries no information its simplification would destroy.

### D7 — Geometry is versioned, because boundaries change by legislation

LGU boundaries and codes change by law, not by revision: provinces split, cities are created,
regions are formed. A geometry is therefore stored with the period it was in force and **is never
mutated in place**. A superseded boundary is retained.

The consequence a reader must see: an event from 1976 attributed to a unit created in 2022 is being
attributed by *today's* boundary. Any containment figure states which edition of the boundary set it
used, and the ingestion records the OSM extract date it came from.

## What must be settled before any code

Implementation is gated on the identity rules, not on the map work. The map work is a week; getting
identity wrong is a defect that spreads into every figure derived from it.

1. **The canonical register edition is fixed and dated** — a specific PSA PSGC publication, cited.
2. **The crosswalk exists and is reviewed** for every unit the active canonical edition holds, with
   every exception in D5 carrying a written reason. **The required population is derived from that
   edition, never written down here.** The register changes quarterly: the mirror this platform first
   loaded reported 17 regions, 81 provinces and 1,634 cities and municipalities, while PSA 2Q 2026
   reports 149 cities and 1,493 municipalities — 1,642 — against 82 provinces and 18 regions. A target
   figure in this document would be wrong within a quarter and would go on being checked against.
3. **The unmatched set is enumerated and accepted** — not zero, accepted. A known unmatched count is
   a fact about Philippine administrative history; a zero achieved by name-matching is a fiction.
4. **The OSM extract is licence-checked and dated**, and registered as a `DataSource` row before any
   geometry is stored, as GEM and the town centres were.

Only then: schema, import, containment query, then the interaction.

## Consequences

**What this buys.** Attribution instead of approximation — "this earthquake is in Carmen" rather
than "within 25 km of Carmen's town centre". Click-the-land identification. Hazard-layer and event
queries scoped to a real administrative unit, which is the unit every Philippine institution plans
in.

**What it costs.** A share-alike obligation on the place database that is now unavoidable rather than
incidental. A crosswalk that must be maintained as the PSA register changes. Roughly 1,650 polygons
of geometry, simplified per zoom band, in a database currently holding 155 fault traces as its
largest vector set.

**What it does not change.** The radius stays. Compare keeps comparing at a shared radius. The
existing caveat about town-centre distances stays true for every figure that is still measured from
a point, and those figures are not quietly re-based onto boundaries.

**What we still refuse.** Barangay boundaries (admin_level 10) are out of scope: coverage in OSM is
uneven across the country, and a boundary set that is complete in Metro Manila and sparse in Caraga
would make the interface most confident exactly where a national platform should not be.
