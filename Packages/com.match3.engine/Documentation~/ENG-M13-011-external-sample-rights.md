# ENG-M13-011 | External Sample Rights and Architecture Intake

**Task:** ENG-M13-011  
**Status:** IMPLEMENTED_UNAUDITED  
**Unity version:** 6000.6.5f1  

---

## Purpose

This document records the license status of four external sample sources evaluated
during engine architecture intake, and establishes the policy for future external
sample intake.

---

## Known External Sources

### 1. Unity GemHunterMatch3 (Unity Gaming Services sample)

- **Source type:** Unity-published sample project
- **License:** Subject to Unity Gaming Services Terms of Service
- **Decision:** Reference-only. No files from this sample may be copied into any
  Match3 engine package until the exact UGS license applicable to the selected files
  is confirmed in writing and recorded in TASKS.md. Contact Unity support for
  confirmation before any reuse.
- **Verdict:** `UgsReviewRequired` — not currently permitted for copy.

### 2. thefcan GitHub Match3 repository

- **Source type:** Public GitHub repository
- **License:** No explicit license file detected in the repository.
- **Decision:** NOT freely copyable. Absence of a license means copyright is retained
  by the author by default. Treat as proprietary until an explicit permissive license
  (MIT, Apache 2.0, BSD, or equivalent) is identified and recorded.
- **Verdict:** `ProprietaryNoReuse` — copying prohibited until license is identified.

### 3. Alperen MIT Match3 code

- **Source type:** Public GitHub repository with MIT License
- **License:** MIT License (see repository LICENSE file for exact text)
- **Repository URL:** Record the exact URL and commit SHA in the TASKS.md entry at
  reuse time.
- **Decision:** May be reused after provenance review. Attribution is required in any
  file that incorporates adapted code: add a comment citing the author, repository URL,
  and license type. The MIT license must be retained or reproduced per its terms.
- **Verdict:** `MitAttribution` — permitted with attribution after provenance review.

### 4. CC BY-NC-ND Gem-Match3 (NonCommercial, NoDerivatives)

- **Source type:** Creative Commons licensed asset or project
- **License:** Creative Commons Attribution-NonCommercial-NoDerivatives (CC BY-NC-ND)
- **Decision:** No copying. The NonCommercial term prohibits use in a commercial game
  product. The NoDerivatives term prohibits creating adapted or derivative works.
  Neither source code nor image assets from this source may appear in any Match3
  game or engine package.
- **Verdict:** `CCByNcNd` — copying prohibited.

---

## Intake Policy

Any external sample, library, or asset considered for integration into this engine
package or any game repository must have a corresponding TASKS.md entry created
**before** any file is committed. The entry must record:

1. Source URL (exact repository or asset page URL)
2. License type (exact SPDX identifier or plain name, e.g. MIT, Apache-2.0, CC-BY-NC-ND-4.0)
3. Specific files considered for reuse (file paths, not whole repos)
4. Attribution plan (how copyright notice and license will be reproduced)
5. Owner approval (explicit sign-off in the TASKS.md entry or referenced issue)

No external file may be committed without this entry and owner approval, regardless
of how permissive the license appears. This policy applies to code, images, audio,
shaders, and configuration files.

---

## Runtime Enforcement

See `Packages/com.match3.engine/Runtime/ExternalSamplePolicy/ExternalSamplePolicyAudit.cs`
for a compilable audit catalog and policy helpers that reflect the decisions above.
