# WholeCart B2B Marketplace — QA Assignment

> **Candidate:** Janaki  
> **Role:** Senior QA Automation Engineer — Cidroy Infotech  
> **Application under test:** http://13.200.185.1/c/janaki-47b177/  
> **Date:** November 2026

---

## Repository Structure

```
WholeCart/
├── README.md                    ← This file
├── defect-log.md                ← 26 defects with steps, evidence, severity
├── docs/
│   ├── test-plan.md             ← One-page test plan (scope, approach, risks)
│   └── assumptions-log.md       ← Questions and working assumptions
├── screenshots/                 ← Evidence screenshots (DEF-01 to DEF-26)
├── automation/
│   └── WholeCart.Automation/    ← C# / .NET 8 / NUnit / Playwright tests
└── ai-sessions/                 ← AI tool session exports
```

## Defect Summary

| Severity | Count |
|----------|-------|
| Critical | 4     |
| Major    | 13    |
| Minor    | 9     |
| **Total** | **26** |

Key findings:
- **DEF-01** (Critical): Inventory oversell allows stock to go negative (-61 pieces), violating BR-01
- **DEF-13** (Critical): IDOR vulnerability — buyers can view other buyers' tax invoices (BR-21)
- **DEF-22** (Critical): Seller cancellation does not propagate to buyer view (BR-13)
- **DEF-06** (Major): Expired pilot promo still waiving delivery fees past 31 Oct 2026 (BR-08)
- **DEF-07** (Major): Tax invoices missing mandatory CR-09 supply details
- **DEF-12** (Major): Minimum order threshold not enforced at ₹2,000 per CR-07

## Round Two — End-to-End Order Flow

The [test case log](docs/test-case-log.md) lists the UI and API scenarios, accounts, and expected results.

| Test | What it verifies |
|---|---|
| `MultiSellerCheckoutCreatesTwoOrdersThatCanBeDeliveredAndInvoiced` | Buyer adds cases from two sellers, verifies checkout totals and a delivery slot, and creates two sub-orders from one checkout. Each seller independently accepts, ships and delivers its sub-order. Buyer history shows each transition and each invoice matches its order total. Covers BR-05, BR-07–09, BR-13, BR-19 and BR-22. |
| `BuyerChecksOutWithPiecesAndCasesOfTheSameProduct` | Individual pieces and cases of the same product remain separate lines in the cart and in one placed order. Checkout totals include both lines, and stock drops by their combined piece count (BR-01, BR-07). |
| `SellerCancellationOfAcceptedOrderAppearsInBuyerHistory` | A seller accepts and cancels a newly placed order. The buyer sees the cancelled status in history and details, and stock returns to its pre-order piece count (BR-11–13, DEF-22). |
| `BuyerCancelsNewOrderAndStockReturnsToItsOriginalPieceCount` | Checkout deducts the exact number of pieces ordered. Immediate buyer cancellation updates history and restores the original piece count (BR-01, BR-11). |
| `CheckoutRejectsOneSellerBelowMinimumEvenWhenCartTotalQualifies` | Checkout explains the per-seller ₹2,000 net minimum and creates no sub-orders when one seller falls below it, even though the combined cart qualifies (CR-07, DEF-12). |
| `BuyerCannotChangeDeliverySlotAfterSellerShipsOrder` | Checkout creates a new order and the seller ships it. The buyer cannot change the slot, and both roles still see the original delivery window and shipped status (BR-15). |
| `AcceptedOrderAppearsInOperatorLogisticsExport` | A buyer places an order, a seller accepts it, and the operator downloads logistics CSV. Checks the required column order and the order's buyer name, delivery date, slot start and end, and piece count (IF-02, DEF-14). |
| `OperatorNavigationIsVisibleOnlyToTheOperator` | The operator can view the marketplace sellers and sees the logistics export control. Buyer and seller navigation omit both controls. This test covers UI visibility. |
| `WholeCartDirectReshipsAndDeliversAfterBuyerRequestsRedelivery` | Buyer places a WholeCart Direct order. Seller1 accepts and ships it, records a failed delivery, then reships and delivers after the buyer requests a different time window. Both roles see the new slot (BR-13, BR-14). |
| `TestBuyerLoginAndNavigation` | Buyer reaches authenticated navigation and opens the catalogue. |

### API tests

The [API tests](automation/WholeCart.Automation/Api) use HTTP requests and run without Chromium.

| Test | What it verifies |
|---|---|
| `BuyerChecksOutAndCancelsItsOwnOrderViaApi` | Buyer adds cases, checks out into an available slot, verifies that order history shows one new order and the cart is empty, then cancels the order and checks its persisted status. |
| `CheckoutApiRejectsCartWhenOnlyOneSellerMissesTheMinimum` | Checkout rejects a qualifying combined cart when one seller is below ₹2,000 and creates no orders (CR-07, DEF-12). |

### Set up and run

Use the .NET 8 SDK and PowerShell 7 (`pwsh`) to install Playwright's pinned Chromium version. From the repository root, run the following setup once.

```bash
dotnet build automation/WholeCart.Automation
pwsh automation/WholeCart.Automation/bin/Debug/net8.0/playwright.ps1 install chromium
```

Set `WHOLECART_PASSWORD` in your shell using the password from the assignment email. In bash or zsh, this prompts without echoing the password.

```bash
printf 'WholeCart test password: '
read -rs WHOLECART_PASSWORD
printf '\n'
export WHOLECART_PASSWORD
```

Run the full suite with one command.

```bash
dotnet test automation/WholeCart.Automation --results-directory TestResults --logger "trx;LogFileName=wholecart.trx"
```

Set `WHOLECART_BASE_URL` to the updated candidate URL when testing a new build. It defaults to the candidate URL above. Set `WHOLECART_HEADED=1` to watch the browser. To run the end-to-end tests, add `--filter 'TestCategory=EndToEnd'`. To run only the checkout-to-invoice path, add `--filter 'FullyQualifiedName~MultiSellerCheckout'`.

To run the two operator tests, use `--filter 'TestCategory=Operator'`.
To run the WholeCart Direct redelivery regression, use `--filter 'TestCategory=Redelivery'`.

Run only the API tests with `WHOLECART_PASSWORD` set.

```bash
dotnet test automation/WholeCart.Automation --filter 'TestCategory=Api' --results-directory TestResults --logger "trx;LogFileName=wholecart-api.trx"
```

Start with empty carts for `buyer1` and `buyer2`. The API tests remove the cart lines they add. The checkout test leaves its cancelled order in history. The API tests look for slots starting tomorrow in India. If the operator has fixed the test store clock to a different date, set `WHOLECART_STORE_DATE` to that date in `yyyy-MM-dd` format. Checkout responses are saved as test attachments under the same evidence root as the browser tests.

### Test data and diagnostics

The UI and API tests load account names, product fixtures, and checkout amounts from [TestData/wholecart.json](automation/WholeCart.Automation/TestData/wholecart.json). Update that file if the assigned build uses different test data. The build copies it into the test output directory. Set the password separately through `WHOLECART_PASSWORD`.

The suite uses separate browser sessions for all seven accounts. It uses `buyer1` for fulfilment and redelivery, `buyer2` for cancellation and logistics orders, `buyer3` for minimum-order validation, the three sellers listed below, and `operator` for dispatch and seller navigation. Start with all three buyer carts empty. The tests calculate case quantities from the published prices, check stock before adding to the cart, and choose a future date with an enabled delivery slot using the application's calendar.

| Catalogue search | Product | Seller |
|---|---|---|
| SKU-0381 | Chef's Cold Coffee | FreshFarm Foods (`seller2`) |
| SKU-0392 | Daily Iced Tea | Coastal Beverages (`seller3`) |
| Hill Toor Dal | Hill Toor Dal | WholeCart Direct (`seller1`) |

Run against the assigned test instance. The tests run serially, create real test orders, and leave order history available for inspection. Delivered test orders consume stock and may use delivery capacity. The minimum-order rejection leaves its items in `buyer3`'s cart. Clear those items before rerunning that case. If a run stops before checkout, clear any test items that remain in that buyer's cart. Compare rejection and cancellation failures with DEF-12 and DEF-22.

### Save screenshots and video

Screenshots are saved automatically at checkout, delivery, invoice, cancellation, stock, validation and operator checkpoints. Failed tests also capture the current page of each actor. The logistics test attaches the downloaded CSV. Use the command below to save the evidence and WebM recordings from every browser session in a known folder.

```bash
WHOLECART_VIDEO=1 WHOLECART_EVIDENCE_DIR="$PWD/TestResults/evidence" \
  dotnet test automation/WholeCart.Automation --results-directory TestResults --logger "trx;LogFileName=wholecart.trx"
```

Each run has its own folder with a subfolder per test. Playwright finalizes the video at the end of each test. Without an explicit `WHOLECART_EVIDENCE_DIR`, the test prints the evidence path under its NUnit working directory. `TestResults/` is ignored by Git.

## Test Accounts

| Role | Username | Business Name |
|------|----------|---------------|
| Buyer | buyer1 | Spice Route Kitchen |
| Buyer | buyer2 | Café Dosa Corner |
| Buyer | buyer3 | शर्मा भोजनालय |
| Seller | seller1 | WholeCart Direct |
| Seller | seller2 | FreshFarm Foods |
| Seller | seller3 | Coastal Beverages |
| Operator | operator | — |

Use the shared test password from the assignment email.

## AI Tools Used

Gemini AI for documentation
