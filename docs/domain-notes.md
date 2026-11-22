# Domain notes: what I researched and how it shaped the design

I come from backend development in other domains, so before designing the API I read up on how time series are used in the Nordic electricity market. These notes describe my understanding and how each point influenced the design. I am sure the team knows this far better, so I would welcome corrections.

---

## 1. Bidding zones

**What I learned:** The Nordic market is split into bidding zones that reflect bottlenecks in the grid. Norway has five (NO1 to NO5), Sweden four (SE1 to SE4), Denmark two (DK1, DK2) and Finland one (FI), so twelve in total. Each zone also has an official EIC code, for example `10YNO-1--------2` for NO1 and `10YDK-1--------W` for DK1.

**How it shaped the design:** Bidding zones are a fixed list (a value object), not free text, so typos are rejected. The API uses the short codes because they are readable; EIC codes are a natural next step for integration with other systems.

## 2. Time series in ENTSO-E documents

**What I learned:** Market data is exchanged as time series. A series has a header (area, business type, unit), a period with a time interval and a resolution, and points numbered by position. The time of a point is calculated as `period start + (position - 1) * resolution`.

**How it shaped the design:** The API models a series the same way, in a simpler form: a header plus `start`, `resolution` and a list of `values`. Gaps and duplicate timestamps are impossible by design. See [decision 2](decisions.md#2-a-series-is-a-header-plus-start-resolution-and-values).

## 3. Resolution and the move to 15 minutes

**What I learned:** The market has been moving from hourly to 15-minute periods. Nordic cross-border intraday trading moved to 15-minute resolution on 18 March 2025 (first delivery day 19 March), and the European day-ahead market (SDAC) followed on 30 September 2025, with the first delivery day on 1 October 2025.

**How it shaped the design:** The API supports both `PT15M` and `PT60M`, using ISO 8601 durations as ENTSO-E does, and checks that `start` is aligned to the chosen resolution.

## 4. Time zones and daylight saving time

**What I learned:** The market day is defined in local time, but local time is awkward to store: a CET/CEST day can have 92, 96 or 100 quarter-hours because of daylight saving changes, and one hour is repeated in October.

**How it shaped the design:** All times are UTC, and intervals are half-open `[from, to)`, so adjacent periods never overlap or leave gaps. See [decision 3](decisions.md#3-utc-everywhere-half-open-intervals).

## 5. Cross-border flows have a direction

**What I learned:** Capacity calculation in the Nordic region is organised around borders between bidding zones (for example DK1 to SE3, or SE3 to FI), and flows across a border have a direction.

**How it shaped the design:** A flow series has a "from" zone (`biddingZone`) and a "to" zone (`toBiddingZone`), with a sign convention: a positive value means flow in that direction, a negative value the reverse. Searching by a zone returns flows at either end, because a user asking about DK1 usually wants both imports and exports.

## 6. Units

**What I learned:** Values can be expressed as power (MW, average over the period) or energy (MWh, total over the period). For a 15-minute period these differ by a factor of four, so mixing them up is a real risk.

**How it shaped the design:** The unit is part of the series and currently fixed to MW. Supporting MWh, or prices in EUR/MWh, would be an extension.

## 7. Why the data matters for grid coordination

**What I learned:** The Nordic TSOs coordinate capacity calculation, outage planning, security analysis and a common grid model across the region. The move to flow-based capacity calculation in the Nordic day-ahead market (first trading day 29 October 2024, for delivery on 30 October) depends on accurate grid and market data, and data is regularly corrected.

**How it shaped the design:** Data quality rules are strict (no overlapping series for the same thing, see [decision 5](decisions.md#5-reject-overlapping-series-with-the-same-identity)), updates are protected against lost changes ([decision 6](decisions.md#6-optimistic-concurrency-with-etag-and-if-match)), and keeping revisions of corrected data is high on my "next steps" list.

---

## Questions I would ask the team

- Which time series do your applications work with most: grid model inputs, flows, capacities, or forecasts?
- Do you store series per day, per business process, or as long continuous streams?
- How are corrections handled today: overwrite, or keep every revision?
- Do you exchange data in ENTSO-E / CIM formats internally, or mostly with external parties?

---

## Sources

- [NEMO Committee: 15-minute MTU in SDAC was implemented](https://nemo-committee.eu/assets/files/15-minute-mtu-in-sdac-was-implemented.pdf)
- [Fingrid: 15-minute intraday cross-border trading in the Nordics](https://www.fingrid.fi/en/news/news/2025/go-live-of-15-min-intraday-cross-border-trading-in-the-nordics-on-1803-for-delivery-on-1903)
- [ENTSO-E: Introduction of different time series possibilities](https://eepublicdownloads.entsoe.eu/clean-documents/EDI/Library/Introduction_of_different_time_series_v1.4.1.pdf)
- [ENTSO-E Transparency Platform: area EIC codes](https://transparencyplatform.zendesk.com/hc/en-us/articles/15885757676308)
- [JAO: Nordic flow-based publication handbook](https://publicationtool.jao.eu/nordic/Nordic_PublicationHandbook)
- [Statnett: Why we have bidding zones](https://www.statnett.no/en/The-power-system/why-we-have-bidding-zones/)
- [Nord Pool: Successful go-live of the Nordic flow-based market coupling project](https://www.nordpoolgroup.com/en/message-center-container/newsroom/exchange-message-list/2024/q4/successful-go-live-of-the-nordic-flow-based-market-coupling-project/)
