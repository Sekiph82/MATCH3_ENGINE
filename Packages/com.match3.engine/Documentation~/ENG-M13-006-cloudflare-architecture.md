# ENG-M13-006 | Cloudflare Workers + D1 + R2 Architecture Design

**Decision:** Owner directive 2026-11-10 V04 — backend is **Cloudflare Workers + D1 + R2** (FINAL).  
**Status:** Design document only. No deployment, no billable infrastructure, no production secrets.  
**Scope:** Reward ledgers, purchase verification, level-pack content delivery for the five Match3 titles.

---

## 1. Cloudflare Workers — Endpoint Design

All routes are served from a single Worker bound to `api.match3.<domain>.com`.  
Authentication uses a per-title API key in `X-Match3-Key` header; the Worker validates against a D1-stored key hash.

### Routes

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/v1/purchase/verify` | Verify receipt from Apple/Google, record entitlement |
| `POST` | `/v1/reward/claim` | Claim a server-side reward (idempotent via `idempotency_key`) |
| `GET`  | `/v1/reward/balance/:player_id` | Read current reward balance |
| `GET`  | `/v1/levelpack/manifest/:game_id` | Fetch current level-pack manifest from R2 |
| `GET`  | `/v1/levelpack/pack/:game_id/:version/:pack_name` | Redirect to signed R2 URL for pack download |
| `POST` | `/v1/player/init` | First-time player registration, returns opaque player_id |
| `GET`  | `/v1/health` | No-auth liveness probe |

### Request/Response Shapes

**POST /v1/purchase/verify**

Request:
```json
{
  "game_id": "Coral_Rescue",
  "player_id": "uuid-v4",
  "receipt": "<base64-encoded store receipt>",
  "store": "apple" | "google",
  "product_id": "match3.coral.rescue.coins100",
  "idempotency_key": "uuid-v4"
}
```

Response (200 OK):
```json
{
  "status": "ok",
  "entitlement_id": "uuid-v4",
  "granted_coins": 100,
  "already_processed": false
}
```

**POST /v1/reward/claim**

Request:
```json
{
  "game_id": "Coral_Rescue",
  "player_id": "uuid-v4",
  "reward_type": "daily_bonus" | "level_complete" | "ad_rewarded",
  "amount": 50,
  "idempotency_key": "uuid-v4"
}
```

Response (200 OK):
```json
{
  "status": "ok",
  "ledger_id": "uuid-v4",
  "new_balance": 1050,
  "already_processed": false
}
```

### Authentication

- Each title has its own `X-Match3-Key` (UUID, stored hashed in D1 `api_keys` table).
- Wonder Pop: separate key; ads/monetization endpoints return 403 if `game_id = Wonder_Pop` hits purchase/reward routes (Category C reserved).
- Key rotation: update D1 row, deploy Worker; old key rejected immediately.

### Retry Policy (Unity SDK side)

- Exponential backoff: 1 s, 2 s, 4 s, max 3 attempts.
- Idempotency key is generated client-side (UUID v4), persisted locally until confirmed.
- 409 Conflict (already processed) is treated as success by the SDK.

---

## 2. D1 Schema

D1 is SQLite-compatible. All tables use `TEXT` UUIDs as primary keys.

### Table: `players`

```sql
CREATE TABLE players (
  player_id    TEXT PRIMARY KEY,
  game_id      TEXT NOT NULL,
  created_at   INTEGER NOT NULL,  -- Unix timestamp
  last_seen_at INTEGER NOT NULL
);
CREATE INDEX idx_players_game ON players(game_id);
```

### Table: `api_keys`

```sql
CREATE TABLE api_keys (
  key_id      TEXT PRIMARY KEY,
  game_id     TEXT NOT NULL UNIQUE,
  key_hash    TEXT NOT NULL,  -- SHA-256 hex of the raw API key
  created_at  INTEGER NOT NULL,
  revoked     INTEGER NOT NULL DEFAULT 0  -- 0=active 1=revoked
);
```

### Table: `reward_ledger`

```sql
CREATE TABLE reward_ledger (
  ledger_id        TEXT PRIMARY KEY,
  player_id        TEXT NOT NULL,
  game_id          TEXT NOT NULL,
  reward_type      TEXT NOT NULL,
  amount           INTEGER NOT NULL,
  idempotency_key  TEXT NOT NULL UNIQUE,  -- prevents double-grant
  created_at       INTEGER NOT NULL
);
CREATE INDEX idx_ledger_player ON reward_ledger(player_id, game_id);
CREATE INDEX idx_ledger_idempotency ON reward_ledger(idempotency_key);
```

### Table: `coin_balances`

```sql
CREATE TABLE coin_balances (
  player_id   TEXT PRIMARY KEY,
  game_id     TEXT NOT NULL,
  balance     INTEGER NOT NULL DEFAULT 0,
  updated_at  INTEGER NOT NULL
);
-- ponytail: separate balance table avoids full ledger scan; recompute from ledger if mismatch detected
```

### Table: `purchase_receipts`

```sql
CREATE TABLE purchase_receipts (
  entitlement_id   TEXT PRIMARY KEY,
  player_id        TEXT NOT NULL,
  game_id          TEXT NOT NULL,
  store            TEXT NOT NULL,  -- 'apple' | 'google'
  product_id       TEXT NOT NULL,
  receipt_hash     TEXT NOT NULL,  -- SHA-256 of raw receipt for dedup
  idempotency_key  TEXT NOT NULL UNIQUE,
  granted_coins    INTEGER NOT NULL,
  created_at       INTEGER NOT NULL
);
CREATE INDEX idx_receipts_player ON purchase_receipts(player_id, game_id);
CREATE INDEX idx_receipts_hash ON purchase_receipts(receipt_hash);
```

### Idempotency Strategy

Every mutating endpoint requires a client-supplied `idempotency_key` (UUID v4).  
The Worker does a `SELECT` by `idempotency_key` before the `INSERT`; if found, returns the original response with `"already_processed": true`.  
D1's serialisable transactions ensure no race: `BEGIN; SELECT … ; INSERT … ; COMMIT;`.

---

## 3. R2 Structure

Bucket: `match3-level-content` (private; accessed via pre-signed Worker URLs).

```
match3-level-content/
  <game_id>/
    manifest.json                      # current pack index (version + pack list)
    v<semver>/
      manifest.json                    # version-pinned manifest snapshot
      <pack_name>.pack                 # binary level pack (zstd-compressed)
      <pack_name>.pack.sha256          # integrity hash
```

**Example:**
```
match3-level-content/
  Coral_Rescue/
    manifest.json
    v1.2.0/
      manifest.json
      levels_1_50.pack
      levels_1_50.pack.sha256
      levels_51_100.pack
      levels_51_100.pack.sha256
```

### Manifest Format (`manifest.json`)

```json
{
  "game_id": "Coral_Rescue",
  "current_version": "1.2.0",
  "packs": [
    {
      "name": "levels_1_50",
      "version": "1.2.0",
      "size_bytes": 204800,
      "sha256": "abc123..."
    }
  ]
}
```

### Access Control

- Worker generates a time-limited (60 s) pre-signed R2 URL per pack download request.
- Unity SDK downloads directly from the signed URL; Worker never proxies binary data.
- Manifests are served as JSON through the Worker (small, no CDN needed at this scale).

---

## 4. Unity SDK Integration Contract

### HTTP Client

- Use `UnityWebRequest` (no third-party HTTP lib needed).
- Base URL read from `RemoteConfig` or a compile-time constant per environment (dev/staging/prod).
- All requests set `X-Match3-Key` from a game-specific constant (non-secret; hashed server-side).
- `Content-Type: application/json` on POST bodies.

### Request Flow (Reward Claim)

```
[Game] → CloudflareSdk.ClaimReward(rewardType, amount)
  → generate idempotency_key (UUID, persist to PlayerPrefs until confirmed)
  → POST /v1/reward/claim
  → on 200: update local balance, clear idempotency_key
  → on 409: treat as 200 (already processed)
  → on 5xx/network error: retry with backoff (max 3 attempts)
  → on 3 failures: queue for next session, keep idempotency_key in PlayerPrefs
```

### SDK Interface (matches ENG-M13-002 contract)

```csharp
// Thin adapter over UnityWebRequest; no SDK-specific dependencies in Engine core.
public interface ICloudflareBackend
{
    UniTask<RewardResult> ClaimRewardAsync(string rewardType, int amount, CancellationToken ct);
    UniTask<BalanceResult> GetBalanceAsync(CancellationToken ct);
    UniTask<PurchaseResult> VerifyPurchaseAsync(string receipt, string store, string productId, CancellationToken ct);
    UniTask<PackManifest> GetLevelManifestAsync(CancellationToken ct);
    UniTask<string> GetPackDownloadUrlAsync(string packName, string version, CancellationToken ct);
}
```

Disabled (Wonder Pop): a `NullCloudflareBackend` implementing the interface returns `PurchaseResult.Disabled` and `RewardResult.Disabled` without network calls. The game layer checks `GameBusinessProfile.MonetizationEnabled` before calling.

---

## 5. Local Development (No Deployment)

### Wrangler Local Mode

```bash
# Install wrangler (dev machine only, not Unity project)
npm install -g wrangler

# Start local Worker + D1
cd workers/match3-api
wrangler dev --local --persist-to ./.wrangler/state

# Apply schema migrations locally
wrangler d1 execute match3-local --local --file=schema/001_initial.sql
```

### Unity-Side Dev Endpoint

`MATCH3_CF_BASE_URL` environment variable (set in `Assets/StreamingAssets/dev-config.json`, gitignored) overrides the Worker URL to `http://127.0.0.1:8787`.

### R2 Mock

For local development, replace the `ICloudflareBackend.GetPackDownloadUrlAsync` response with a local `file://` path pointing to `Assets/StreamingAssets/TestPacks/`. The `LocalR2Mock` adapter reads files from disk; no Wrangler R2 emulation required at this stage.

---

## 6. No-Cost Protections

- Zero Cloudflare account activation or deployment in this document. Design only.
- No secret keys, tokens, or Cloudflare account IDs are present here.
- Schema migrations in `workers/match3-api/schema/` are SQL files only; no migration runner is activated.
- Production deployment requires explicit owner authorization (a separate ENG task).
