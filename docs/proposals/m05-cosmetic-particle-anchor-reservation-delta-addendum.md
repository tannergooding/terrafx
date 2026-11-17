# M05 anchor reservation delta and protected-phase gap

## Result

**No complete reservation delta or protected interval is qualified. Hooks remain closed.** The exact known standing-data term is **10,592 per selected World**, also charged **once** to that domain's data ledger. The selected anchor retains **one original O**, adding **zero O cells**, not 128. Neither the managed O-cell reduction nor a copied grant funds that data.

The domain's **+72 shallow increment** is an additional coverage input inside the registrar-control catalogue, not automatically another +72 reservation: its existing named control reservation remains 4096. The 72-byte anchor is inline in that domain increment and must not be added again.

There is a newly identified accounting prerequisite: the declaration candidate inherits a **`sizeof`-dependent O-slab charge**, so its computed process charge would fall with managed stride. Preserve Chapter 04's existing charged row before any operational promotion; do not infer unchanged accounting from unchanged helper text.

This addendum supplies the bounded derivation and rectification, not a new allowance or permission to operate the candidate.

## 1. Counts, managed size and charged storage

Scope remains one fresh selected standalone World, its private default domain, one standing owner/control/backing, no acquired cosmetic use and no Scene/pool execution.

| Item | Exact selected count / incremental effect |
|---|---|
| Original binding O | One, already issued by real fresh admission. Anchor attachment adds zero K/O/P/U and zero domain interests. O/key retention lasts longer; no new quota is minted. |
| Fresh process prefix | Existing K/O/P/U = 1/1/1/2. Normal commit releases P/coordinator U. This is not a second reservation for the anchor. |
| Cosmetic credit cells | 128 entries in one World-owned array; not 128 O cells, standing owners, Tasks or acquired independent uses. |
| Domain anchor value | One 72-byte inline value. No separate 72-byte heap object was observed or selected. |
| Domain | One; shallow instance 272 -> 344. Its referenced registry is the existing process registry, not a newly allocated domain-owned copy. |
| Shared registration increment | Separate +8 per actual shared registration object, outside this per-World physical row. |

`TryReserveFresh` and `TryCommitFresh` establish the process counts above. Actual process backing is installed in **256-cell pages**, retained at high water. One occupied O does not cause a 128-byte per-World allocation/debit. The number of pages already installed/free is process state; no current value is assumed. Holding the original O can make a later admission reach its existing page/count limit sooner, but cannot increase that limit.

### The O-charge consequence that layout tests did not execute

`WorldGuidRegistry.GetSlabCharge(SlabCategoryBinding)` currently calls:

```text
ArrayCharge(CellsPerSlab, Unsafe.SizeOf<BindingCell>())
```

`EnsureCellAvailability` uses that result for the pending slab grant and `s_chargedBytes`. Thus, for the exact measured X64 lane:

| Calculation for one 256-cell O page | Bytes |
|---|---:|
| Current baseline: 64 + RoundUp16(256 * 128) | 32,832 |
| Declaration candidate's unchanged helper: 64 + RoundUp16(256 * 48) | 12,352 |
| Candidate computed-charge change for that newly allocated page | -20,480 |
| Chapter 04's retained O charged-row requirement | 32,832 |

These are **source-derived arithmetic results**, not executed reservations. Metadata `Size=128` does not override the observed managed 48-byte stride used by `Unsafe.SizeOf`. For a fixed number of such pages the helper's difference scales by that page count, not by the number of particle credits. Already installed pages do not refund capacity when a cell is freed.

The layout record's `LogicalReservationsChanged=false` describes no live accounting edit/operation. It is **not evidence** that this candidate helper would compute the old charge. Preserve the frozen record; this source-derived consequence narrows its interpretation.

**Select the minimal charge-row rectification:** decouple the O **charged stride** from managed stride using Chapter 04's existing 128-byte reservation, as the domain already does for its named lineage-cell charge. Preserve all page/count ceilings and declared process backing totals. Do not add padding fields or lower the accounting schema as part of this work. Physical/GC-layout and runtime-control coverage still need their own proof.

With that rectification and the same page membership, the anchor's incremental O-backing **reservation** is zero. Without it, the candidate silently changes the frozen process accounting and cannot be promoted. Under neither interpretation is process metadata a World/domain credit.

## 2. Exact known World/domain terms

The prior standing observations give:

```text
one Cell backing accounting input            10,304
one credit-owner shallow input                 136
one standing-control shallow input             144
one common lifetime-reference increment           8
known World-associated standing term          10,592
```

This is the previously selected **additive standing-data input**, not a uniform GC-allocation measurement or complete construction footprint. No existing-control overlap has been proved deductible from it. The lifetime increment is included once; the shared registration increment is not included.

For that policy, the original World preclaim must include the known 10,592 **before** selected allocation and consistent live/retained World attribution must include the same original claim. Domain `DataCharge` includes that World reservation, so its known increment is also 10,592—not a second physical allocation or a further 10,592 to add on top.

The domain's 72-byte shallow increment has different accounting:

- `GetRegistryCharge` remains `RegistrarControlBytes + GetRegistryArrayCharge(...)`, with `RegistrarControlBytes = 4096`.
- The declaration changes no domain page/cell type or requested interest count. For the same capacities, its computed registrar charge is unchanged, despite the larger shallow domain.
- Coverage must establish whether the **complete** modified domain control/protected catalogue fits that existing 4096 row. A 344-byte shallow instance proves neither its Task/children nor its constructor/publication/failure envelope.
- If existing named coverage suffices, the additional registrar reservation for this representation can be zero. Otherwise derive the exact uncovered control/protected rows and preadmit them under the existing metadata B and aggregate domain limit. Do not guess +72 as the complete shortfall, or charge a second carrier.

Consequently, **10,664 = 10,592 + 72 is only a cross-owner sum of given sizing inputs**, not an exact World charge, a homogeneous managed-allocation delta or a complete domain admission amount. Subtracting 80 for “one saved O cell,” adding another 72 for the inline value, or multiplying it by 128 would be incorrect.

The three existing 4096 figures are also distinct:

| Existing reservation | Ledger |
|---|---|
| `World.FreshWorldControlBytes` | World/data construction row |
| `WorldAdmissionDomain.RegistrarControlBytes` | Domain registry metadata |
| Process registry control/bootstrap | Independent process metadata |

None is demonstrated spare capacity for either of the others.

## 3. What an exact future preclaim must establish

Use the following accounting variables, **not selected byte allowances**:

- `PW`: additionally required World-owned protected/retained provision beyond the known standing term and demonstrated baseline coverage.
- `PD`: additionally required registrar-owned control/protected provision beyond demonstrated existing domain coverage; it already includes any uncovered part of the +72 representation input.
- `PG`: additionally required process-owned invoker/GC/control coverage beyond its demonstrated existing declared backing/control. It is separate from World/domain funding.

For simultaneously held original provisions, the candidate's incremental rows are:

```text
World standing/protected preclaim       = 10,592 + PW
same domain's data increment            = 10,592 + PW
domain metadata/protected increment     = PD
domain aggregate increment              = 10,592 + PW + PD
process anchor count increment          = 0
process backing increment at same pages = 0, after preserving the 128-byte charge
other process protected coverage        = PG, never borrowed by the domain
```

No value for `PW`, `PD` or `PG` is established. Zero is admissible only after actual disjoint existing coverage is demonstrated. If a term is represented as another kind of existing domain-owned credit, retain its correct quota/category rather than bypassing metadata B through `_worldBytes`.

These equations count **held grants once**. Allocation spends an original preclaim; installation/live attribution is not another debit. Constructor and close scratch are not blindly summed if a single actual prepaid provision safely serves nonoverlapping roles. Conversely, different World/domain protected grants that remain reserved together must coexist in admission even when their code executes at different times.

Admission must still satisfy the exact configured World-state limit, domain metadata B = `min(134217728, MaximumBytes)`, aggregate domain limit and process K/O/P/U/G/B ceilings. The default 2-GiB World/8-GiB domain ceilings do not establish headroom or permit upward rounding.

`TryReserveWorld` can preclaim a **known complete** amount within remaining aggregate domain headroom. It cannot determine the missing terms. Current `World.AdmitWorld` contains no standing term, and `GetWorldOwnedStateBytes` contains no matching attribution. The later `ConfigureOwnedStateBudget` zero-clamp is not initial admission. A selected preflight must check the complete amount against the World limit before allocation; otherwise refuse without changing caps.

## 4. Exact uncovered phases

| Real phase | Required original coverage / exact gap |
|---|---|
| Before default-domain construction | Current `World.AdmitWorld` constructs the domain/default owner **before** obtaining the World reservation. Its later 10,592 preclaim cannot fund this earlier interval. The proposed prospective registrar-control grant must cover actual construction/partial-construction failure and be bound to the prepared original O before escape. Its finite amount/end remain absent. |
| Standing construction and installed lifetime | One owner, one control and one fixed array are known. Additional cold/native children, escaping failure/cleanup graphs and held constructor/close provision are not. An O exception **reference** does not fund the referenced exception graph. Required `PW` and exact simultaneous retained/unspent coverage remain open. |
| World data end -> domain publication | Original World bytes/interests can end before the remaining World caller/finally work. Either that work ends before debit or an exact funded handoff covers it. An O -> domain reference does not itself retain/fund the World lifetime/standing graph. The selected World boundary/receiver must close before those bytes can end. |
| Zero interests, publisher in flight | Current `ReleaseRegistry` zeros registrar charge before the publisher at the seven traced entries. The proposed domain claim must instead survive actual publisher/body/gate return. `PD`'s held floor, complete Task/runtime/native tail, unique-publisher proof and harmless suffix are not qualified. |
| Failed close / terminal accounting | Original O retention must remain within process counts and preserve its key role; World/domain debts keep their actual issuers until end/transfer. Exact terminal commit, same-exception retention, no-retry behavior, process-side `PG` and final post-debit access boundary remain unimplemented/unqualified. |

This identifies missing coverage for the new selected profile; it does not reclassify legacy functional tests as allocation/return qualification.

**Distinguishing admission example:** exactly 10,592 of remaining World/domain data headroom admits only the known data row. It cannot establish admission of an unknown positive protected term or an uncovered metadata row. The O helper's 20,480-per-page reduction cannot repair either shortfall because it belongs to a different ledger and would violate the retained charged-row selection.

## 5. Smallest next closure and stop

No further owner/header probe is selected here. The next bounded source/evidence work is:

1. Preserve the existing O logical charge independently of the measured managed size; a later pure accounting/layout check must distinguish 128-byte reservation from 48-byte managed stride. No registration/release/publisher workload is needed for that arithmetic closure.
2. Close one exact **phase-indexed original-claim catalogue** for the selected fresh empty-registration route: creator, actual graph/retained receiver, byte/count row, simultaneous allocated/unspent/held provision, and original end/transfer. Resolve the specific `PW`/`PD`/`PG` and before-World-construction entries above rather than add a margin.
3. Implement original World data preclaim/attribution and the lower registrar terminal provision only after those rows are complete. No operation credit, extra I/U or World-interest retention may be used to manufacture publication readiness or hide a missing metadata grant.

The [terminal-anchor disposition](m05-cosmetic-particle-terminal-anchor-disposition-addendum.md) still owns O anchoring/failure policy; the [terminal-return ledger](m05-cosmetic-particle-terminal-return-ledger-addendum.md) owns the publication/caller edges. No Task observer, new acceptance ID, public API, cap increase, live storage hook or pool execution is selected.

**Current answer:** existing reservation mechanisms can admit a complete derived profile without raising caps **if** it fits. The inspected reservations do not currently cover/prove this complete profile. Keep standing construction, the terminal invoker, all seven publisher integrations and downstream World/pool hooks closed.

## Input boundary

These source/evidence hashes identify the bounded derivation. No method is invoked to obtain a charge, no probe/build/test is run, and the frozen layout artifacts are not edited.

| Input | SHA-256 |
|---|---|
| `sources\TerraFX.Runtime\Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `2CE1C0B90157ADE6F82605409AB599F7415B7D8AA0A63A4BF4327263610248D9` |
| `sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs` | `9A7339A8ECFD56C60F7C2A5C9251BCBBA2DA73A7A26520AFCDBE533B24BD39E9` |
| `sources\TerraFX.Runtime\Entities\WorldCosmeticCreditOwner.cs` | `7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\proposals\m05-cosmetic-particle-terminal-anchor-disposition-addendum.md` | `FC1DED883EA5CF6A37214B595169457D6A92610AEC0908F4DE0ADE2304508C1D` |
| `docs\proposals\m05-cosmetic-particle-terminal-return-ledger-addendum.md` | `0A317BE624225E5A6873F82D491FDD93B2C01A91DCBAF1CF96153ADFE4659DB8` |
| `artifacts\qcl\pe\results.json` | `3D18CB2E432471E653A33EC1C987972D589AEC1186859DCB74879F488A45266A` |
| `artifacts\qcl\pe\baseline-inputs.json` | `9FA3DFA22ED2B7FAAA36692AF3D38BE51B52C671C6201FE535B3F3D4534400C1` |
| `artifacts\qcl\pe\candidate-inputs.json` | `E44381823E6A08A7CCCC9611DDB8CABC01FB0A23694CF09DCAF9E7FE1D4B9BC8` |
| `artifacts\qcl\pe\candidate-production.patch` | `9738F791C53ACC799561DAC4CC46198FDCC06BAACBA03E7AE7D163B2622B5D5F` |
| `artifacts\qcl\p1\sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs` | `D38B7DDDB838181F7C278C31147E0C85CF05AC0BF45030497DEBE921A44F40F1` |
| `artifacts\qcl\h\results.json` | `D69C5C2D69517FEE0AD42D4B63270A3B01628DEBC9CFC9A159E4918C6A2AA916` |

Only this proposal is added. No source/tests or maintained chapters/roadmap change, and no World/domain/registry/release/Task/publisher/Scene/pool operation runs.
