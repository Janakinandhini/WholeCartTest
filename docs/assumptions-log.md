# Questions & Assumptions Log — WholeCart Marketplace v1.0

Since no live support is available during the testing window, all ambiguities encountered in the Functional Design Document (`WC-FDD-1.0-22db`) and Release Notes (`WC-RN-1.0-22db`) have been documented below as explicit assumptions.

| ID | Module / Spec Ref | Question / Ambiguity | Working Assumption |
| :--- | :--- | :--- | :--- |
| **A-01** | BR-06 / BR-24 | How does the store clock control (BR-24) interact with the 10:00 AM same-day delivery cut-off rule (BR-06)? | Same-day delivery cut-off evaluates against the active server store clock time. When an operator shifts the store clock, the 10:00 AM threshold shifts proportionally relative to that set time. |
| **A-02** | CR-07 / BR-04 | Does the ₹2,000 net minimum order requirement apply per seller sub-order *before* or *after* applying product discounts or taxes? | The ₹2,000 threshold applies strictly to the **net value (excl. tax)** of all line items combined for that specific seller sub-order in the checkout. Taxes and delivery fees are excluded from this calculation. |
| **A-03** | BR-11 | Is the 5-minute cancellation window measured from the exact timestamp the order HTTP POST request returns a success response? | Yes, the 5-minute countdown starts immediately upon successful order creation (`T=00:00`). The UI/API blocks cancellation at `T=05:01` server time. |
| **A-04** | IF-01 | How should the seller CSV import handle trailing whitespace or empty optional columns? | Rows with missing mandatory columns (`sku`, `piece_price`, `stock`) are treated as invalid and rejected with a line number error; optional columns default to empty/zero. |
| **A-05** | BR-17 | Can partial refunds be requested multiple times on the same order line up to the total ordered quantity? | Yes, partial refunds are cumulative, provided the sum of all refunded quantities across requests never exceeds the original line quantity. |