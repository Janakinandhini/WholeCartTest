 # One-Page Test Plan — WholeCart B2B Marketplace v1.0

## 1. Scope
* **In-Scope:**
	* Buyer workflows: Catalogue browsing, multi-seller cart management, multi-seller checkout, order history, order cancellation (5-min window), slot modification, redelivery requests, reordering, refunds/exchanges, tax invoices, monthly statements, and inquiries.
	* Seller workflows: Order lifecycle management (accept, ship, deliver, fail, cancel), product catalogue management, CSV batch import (`IF-01`), and Partner API (`IF-03`).
	* Operator workflows: Marketplace settings, seller approvals, logistics CSV export (`IF-02`), and store clock manipulation (`BR-24`).
* **Out-of-Scope:** Underlying infrastructure penetration testing, third-party payment gateway uptime, and native mobile wrapper apps.

## 2. Approach
* **Exploratory & Functional Testing:** Systematic validation of all business rules (BR-01 to BR-24) and Change Requests (CR-07, CR-09, CR-11) using assigned test accounts (`buyer1`–`buyer3`, `seller1`–`seller3`, `operator`). Retrieve credentials from the approved secrets store; do not include passwords in this document.
* **Boundary Value Analysis:** Testing exact numerical thresholds for cart net minimums (₹2,000 per CR-07), delivery fee cutoffs (₹3,000 per BR-08), inventory limits (BR-01), and the 5-minute cancellation window (BR-11).
* **Integration Testing:** Verifying UTF-8 CSV import error reporting (`IF-01`), operator logistics export columns (`IF-02`), and Partner API pagination (`IF-03`).
* **Load Testing:** In an explicitly authorized, isolated test environment only, simulate concurrent requests up to the approved 50 requests/sec limit while monitoring latency (`NF-01`, `NF-02`, `NF-03`). Obtain written approval, coordinate a test window, and define stop conditions before running; do not load-test the production or public environment.

## 3. Entry & Exit Criteria
* **Entry Criteria:**
	* An approved, isolated test environment URL is stable and accessible; confirm the target is authorized before testing.
	* All test accounts authenticate successfully using credentials retrieved from the approved secrets store.
* **Exit Criteria:**
	* 100% execution of critical end-to-end test scenarios.
	* Zero open Critical or High severity defects without an approved mitigation/workaround.
	* All deliverables (Test plan, defect log, test cases, automation, load test results, and recording) completed and committed to GitHub without secrets or sensitive customer data.

## 4. Key Risks & Mitigation
| Risk | Impact | Mitigation Strategy |
| :--- | :--- | :--- |
| Store clock manipulation (`BR-24`) affecting concurrent test runs | High | Coordinate testing windows or use isolated test sequences when verifying time-dependent rules like slot cut-offs. |
| Concurrent database state changes across buyers | Medium | Use dedicated seller accounts and unique SKUs per test suite to prevent inventory collision. |
| Load testing may affect service availability | High | Restrict load testing to the approved isolated environment, obtain written approval, monitor service health, and stop at predefined thresholds. |