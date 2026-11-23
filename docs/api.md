# API reference

Base path: `/api/v1/time-series`. All timestamps are UTC in ISO 8601 format and are returned with `Z`. Request timestamps must include an offset, and only `Z` (`+00:00`) is accepted. All errors use [Problem Details (RFC 9457)](https://www.rfc-editor.org/rfc/rfc9457).

---

## The time series resource

| Field | Type | Notes |
|---|---|---|
| `id` | GUID | Assigned by the server |
| `type` | `Production` \| `Consumption` \| `CrossBorderFlow` | |
| `biddingZone` | string | One of the 12 Nordic zones. For flows, the "from" zone |
| `toBiddingZone` | string or null | Required for `CrossBorderFlow` and must differ from `biddingZone`; must be null or omitted for other types |
| `unit` | string | `MW` |
| `resolution` | string | `PT15M` or `PT60M` |
| `start` | datetime (UTC) | Must be aligned to the resolution |
| `end` | datetime (UTC) | Calculated: `start + number of values * resolution`. Response only |
| `values` | number[] | One value per period, no gaps; 1 to 35,136 values, each with at most 3 decimal places (1 kW). Production and consumption values must be zero or positive; flow values may be negative (reverse direction). Request only |
| `points` | `{ timestamp, value }[]` | Calculated from `start`, `resolution` and `values`. Response only; left out of searches without a window |

A request body may contain only the request fields. An unknown field, such as a typo or a response-only field like `id`, returns **400** naming the field, so send only the request fields when updating a series you have read.
| `version` | integer | Increases on every update. Also returned as the `ETag` header |

---

## Create a series

```http
POST /api/v1/time-series
Content-Type: application/json

{
  "type": "Production",
  "biddingZone": "SE3",
  "unit": "MW",
  "resolution": "PT60M",
  "start": "2026-11-27T00:00:00Z",
  "values": [10234.0, 10120.5, 9987.2]
}
```

**201 Created**

```http
Location: http://localhost:5080/api/v1/time-series/6f1c2a7e-0d7b-4c8e-9a1f-2b3c4d5e6f70
ETag: "1"
```

```json
{
  "id": "6f1c2a7e-0d7b-4c8e-9a1f-2b3c4d5e6f70",
  "type": "Production",
  "biddingZone": "SE3",
  "toBiddingZone": null,
  "unit": "MW",
  "resolution": "PT60M",
  "start": "2026-11-27T00:00:00Z",
  "end": "2026-11-27T03:00:00Z",
  "version": 1,
  "points": [
    { "timestamp": "2026-11-27T00:00:00Z", "value": 10234.0 },
    { "timestamp": "2026-11-27T01:00:00Z", "value": 10120.5 },
    { "timestamp": "2026-11-27T02:00:00Z", "value": 9987.2 }
  ]
}
```

---

## Get a series by id

```http
GET /api/v1/time-series/{id}
```

**200 OK** with the series and an `ETag` header, or **404 Not Found**.

---

## Search

```http
GET /api/v1/time-series?biddingZone=DK1&type=CrossBorderFlow&from=2026-11-27T00:00:00Z&to=2026-11-27T06:00:00Z&page=1&pageSize=50
```

| Parameter | Required | Meaning |
|---|---|---|
| `biddingZone` | No | Matches `biddingZone` or `toBiddingZone`, so flows into and out of the zone are both returned |
| `type` | No | `Production`, `Consumption` or `CrossBorderFlow` |
| `from`, `to` | No | Half-open interval `[from, to)` as UTC timestamps (for example `2026-11-27T00:00:00Z`). Give both or neither; `from` must be before `to` and the window can be at most 31 days |
| `page`, `pageSize` | No | Defaults 1 and 50; `page` at least 1, `pageSize` 1 to 200 |

Behaviour:

- A series is returned if its period **overlaps** `[from, to)`. Results are ordered by `start`, then type and zones, so paging is stable.
- Invalid parameters return **400** with an error per parameter.
- The returned `points` are **clipped** to the requested window, so the client gets exactly the window it asked for: a point is included when its timestamp lies in `[from, to)`.
- Without `from` and `to`, series are returned **without `points`**, because a page of year-long series would be hundreds of megabytes. Use a window, or get a series by id.

**200 OK**

```json
{
  "items": [ { "id": "...", "type": "CrossBorderFlow", "biddingZone": "DK1", "toBiddingZone": "SE3", "points": [ ] } ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 1
}
```

---

## Update a series

```http
PUT /api/v1/time-series/{id}
Content-Type: application/json
If-Match: "1"

{ ...same body as create... }
```

- `PUT` replaces the data of a series: `start`, `unit` and `values`. Its identity (`type`, `biddingZone`, `toBiddingZone`, `resolution`) is fixed: a different value returns **400**, so create a new series instead.
- `If-Match` is optional. If it is sent and does not match the current version, the API returns **412 Precondition Failed** and nothing changes. `*` means any version. A value that is not a single version ETag such as `"1"` returns **400**.
- Without `If-Match` the last write wins, but an update is still never lost silently: if another request changes the series while this one runs, the API returns **412**.
- **200 OK** with the updated series and the new `ETag`. Other errors: **400**, **404**, **409**.

---

## Delete a series

```http
DELETE /api/v1/time-series/{id}
If-Match: "2"
```

**204 No Content**, or **404 Not Found**. `If-Match` is optional and works as for `PUT`: a stale version returns **412** and the series is kept, so nobody deletes a series that was just corrected.

---

## Errors

| Status | When |
|---|---|
| `400 Bad Request` | Validation failed; details per field in `errors` |
| `404 Not Found` | The series does not exist |
| `409 Conflict` | The series would overlap an existing series with the same type, zones and resolution |
| `412 Precondition Failed` | `If-Match` does not match the current version |
| `500 Internal Server Error` | Unexpected error; no internal details are exposed |

**Validation error example**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "start": [ "Start must be aligned to the resolution (PT15M)." ],
    "toBiddingZone": [ "ToBiddingZone is required for CrossBorderFlow." ]
  }
}
```

**Conflict example**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Overlapping time series",
  "status": 409,
  "detail": "A CrossBorderFlow series for DK1 to SE3 at PT15M already covers part of 2026-11-27T00:00:00Z to 2026-11-27T01:00:00Z."
}
```
