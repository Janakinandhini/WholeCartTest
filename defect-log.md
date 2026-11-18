
### DEF-01: Order Placement Allows Inventory Oversell Into Negative Stock

* **Rule / Spec Violated:** `BR-01` — Stock is held in pieces. A case contains the product's case size in pieces. An order can never take more pieces than are in stock, including when two buyers order at the same moment.
* **Severity:** `Critical`
* **Component / Layer:** Backend / API (`POST /api/cart/items`)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)`
* **Preconditions:** Chef's Butter (`SKU-0293`, `product_id: 293`, `case_size: 6`) has an initial inventory of `518` pieces (`86` maximum full cases = `516` pieces).
* **Steps to Reproduce:**

1. Authenticate as buyer and populate the cart via `POST /api/cart/items` with cases of Chef's Butter (`product_id: 293`, `case_size: 6`) whose combined piece total exceeds `518` pieces.
2. Submit checkout requests to `POST /api/orders` with valid `slot_date` and `slot_start` parameters.
3. Inspect the updated inventory balance for `product_id: 293` via the API.

* **Actual Result:** Orders exceeding the available `518` pieces are accepted and deducted from inventory. Chef's Butter (`product_id: 293`) drops to a negative inventory balance of `"stock": -61` (`61` pieces oversold).
* **Expected Result:** Total committed pieces across confirmed orders must never exceed the `518` pieces in stock. Excess or concurrent orders exceeding available piece stock must be rejected atomically (`HTTP 400 / 409 / 422`) so inventory never drops below `0`.

---

### DEF-02: Add-to-Cart Accepts Case Quantities Exceeding Stock and When Stock Is Negative (-61)

* **Rule / Spec Violated:** `BR-01` — Stock is held in pieces. A case contains the product's case size in pieces. An order can never take more pieces than are in stock.
* **Severity:** `Major`
* **Component / Layer:** UI (Chef's Butter product page) & API (`POST /api/cart/items`)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer1`
* **Preconditions:** Chef's Butter (`SKU-0293`, `product_id: 293`) has `case_size: 6`.
* **Steps to Reproduce:**

1. **UI Flow:** Log in as `buyer1`, open Chef's Butter (`SKU-0293`) while stock is `518` pieces, select Case, and add more than `86` cases (`> 518` pieces) to the cart.
2. **API Flow:** Authenticate as `buyer3` (`x-wc-actor: buyer3`) with `50` cases of `product_id: 293` in the cart, and call `POST /api/cart/items` with:
```json
{
  "product_id": 293,
  "unit": "case",
  "qty": 43
}

```



* **Actual Result:**
* **UI:** The Cart accepts quantities exceeding `518` pieces without displaying any stock validation warning.
* **API:** `POST /api/cart/items` returns `HTTP 200` and accumulates `"qty": 93` cases (`93 × 6 = 558` pieces, `"line_net": "25411.32"`) even though the same response shows the product's current stock is `"stock": -61`.


* **Expected Result:** Both the UI and `POST /api/cart/items` must convert requested cases to pieces (`qty × case_size`) and reject requests with an `HTTP 400 / 422` validation error whenever cumulative cart pieces exceed available piece stock or when stock `<= 0`.

---

### DEF-03: UI Checkout Fails Silently Without Error Message When Submitting an Over-Limit Cart

* **Rule / Spec Violated:** `BR-01` (Buyer-facing stock validation feedback during order submission)
* **Severity:** `Critical`
* **Component / Layer:** Frontend / UI (Cart → Checkout flow)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer1`
* **Preconditions:** Chef's Butter (`SKU-0293`) has `518` pieces in stock (Case of 6), and `buyer1` has added more than `518` pieces to Cart.
* **Steps to Reproduce:**

1. Log in as `buyer1` and add more than `518` pieces of Chef's Butter (`SKU-0293`) to Cart.
2. Open Cart from the top navigation bar and attempt to place the order.
3. Check the current page, Orders, and Cart.

* **Actual Result:** Order is created under Orders with zero error or validation feedback shown to the buyer.
* **Expected Result:** When an over-limit cart cannot be checked out, the UI must keep the buyer on the Cart / checkout screen and display a clear error message stating that the requested quantity exceeds the `518` pieces currently in stock.

---

### DEF-04: Order Confirmation Banner Missing Generated Order Number

* **Rule / Spec Violated:** Functional Design Document `§5.2 Checkout` (`On success go to Order history with the message: "Your order has been placed. Order number: {numbers}"`)
* **Severity:** `Minor`
* **Component / Layer:** Frontend / UI (`Checkout` → `Order history`)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`Test@123`)
* **Preconditions:** Cart contains a valid order meeting `BR-01` and `BR-04`/`CR-07` (`>= ₹2,000` net per seller).
* **Steps to Reproduce:**

1. Log in as `buyer3` and add `6` cases of **Chef's Cold Coffee** (`SKU-0381`, **FreshFarm Foods**, `₹2,947.68` net / `₹3,596.26` total) to **Cart**.
2. Proceed to **Checkout**, select a valid delivery date (`2026-11-18`) and slot (`08:00–11:00`), and click **`Place order`**.
3. Inspect the confirmation banner at the top of the **Order history** page.

* **Actual Result:** **Order history** displays a green confirmation banner reading **`"Order placed!"`** without including the generated order number (`WC-01008-1`).
* **Expected Result:** Per Functional Design Document `§5.2`, the confirmation banner must display **`"Your order has been placed. Order number: {numbers}"`** (e.g., `"Your order has been placed. Order number: WC-01008-1"`).

---

### DEF-05: Cart Quantity Control Uses Numeric Input + `Update` Button Instead of `1–20` Dropdown

* **Rule / Spec Violated:** Functional Design Document `§5.1 Cart` (`Quantity: Dropdown, 1–20 — Changing it updates the line; larger quantities are added from the product page`)
* **Severity:** `Minor`
* **Component / Layer:** Frontend / UI (`Cart` screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`Test@123`)
* **Preconditions:** At least one product is added to the buyer's **Cart**.
* **Steps to Reproduce:**

1. Log in as `buyer3` and add any product (e.g., **Chef's Namkeen Mix**, unit `case`) to **Cart**.
2. Navigate to **`Cart`** from the top navigation bar.
3. Inspect the **Quantity** column for the cart line item.

* **Actual Result:** The **Quantity** column renders a free-text numeric input box (displaying `2`) alongside an **`Update`** button.
* **Expected Result:** Per Functional Design Document `§5.1`, the **Quantity** column must render a **`Dropdown, 1–20`** that automatically updates the line total when a new quantity is selected.

---

### DEF-06: Expired Pilot Promo Still Waiving Delivery Fee in November 2026

* **Rule / Spec Violated:** `BR-08` (Delivery fee: `₹100` plus `18%` tax = `₹118.00` for each seller sub-order whose net value is below `₹3,000`) & `Pilot OPS-14` promotional expiration date (`31 Oct 2026`)
* **Severity:** `Major`
* **Component / Layer:** Backend / Checkout Pricing (`Checkout`)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`Test@123`) | Store Date: `17/11/2026`
* **Preconditions:** Current store date is `17 Nov 2026` (past the `31 Oct 2026` expiration date of `Pilot OPS-14`), and the seller sub-order net value is below `₹3,000`.
* **Steps to Reproduce:**

1. Log in as `buyer3` on `17 Nov 2026` and add `4` cases of **Chef's Cold Coffee** (`SKU-0381`, **FreshFarm Foods**, `Net: ₹1965.12`, which is below `₹3,000`) to **Cart**.
2. Proceed to **Checkout** and inspect the promo banner and **Delivery (incl. tax)** column.

* **Actual Result:** Checkout displays the active green banner **`"Pilot OPS-14: free delivery on all your orders until 31 Oct 2026."`** and sets **`Delivery (incl. tax)`** to **`₹0.00`** instead of `₹118.00`, even though `31 Oct 2026` has already passed.
* **Expected Result:** After `31 Oct 2026`, `Pilot OPS-14` must expire and every seller sub-order with a net value below `₹3,000` must be charged **`₹118.00`** (`₹100` + `18%` tax) per `BR-08`.

---

### DEF-07: Tax Invoice Missing Mandatory `Supply type: B2B` and `Place of supply` Fields (`CR-09`)

* **Rule / Spec Violated:** `CR-09 — Tax invoice — mandatory supply details` (Every tax invoice must show `1. Supply type: B2B` and `2. Place of supply: state name and state code`, taken from the buyer's GSTIN)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (`Tax invoice` screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`शर्मा भोजनालय`, GSTIN `30AAFCB3333C1Z3`, `Test@123`)
* **Preconditions:** Buyer `buyer3` (registered in Goa with GSTIN `30AAFCB3333C1Z3`) has a delivered order (`WC-01006-1`) with generated tax invoice **`INV-00006`**.
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`).
2. Navigate to **`Invoices`** and open **`Tax invoice INV-00006`** (for Order **`WC-01006-1`**, Seller: **Coastal Beverages**).
3. Inspect the invoice header and body details for supply metadata.

* **Actual Result:** Neither **`Supply type: B2B`** nor **`Place of supply: Goa (30)`** (derived from buyer GSTIN state code `30`) is displayed anywhere on **`Tax invoice INV-00006`**, rendering the tax invoice invalid for Input Tax Credit.
* **Expected Result:** Per `CR-09`, every tax invoice must display **`Supply type: B2B`** and **`Place of supply: Goa (30)`** (state name and state code extracted from the buyer's GSTIN).

---

### DEF-08: Seller GSTIN Missing on Tax Invoice

* **Rule / Spec Violated:** `BR-19` (An invoice must show the seller's name and GSTIN, the buyer's name, GSTIN and address) & Functional Design Document `§5.4 Invoice` (Top left: `Seller name, Seller GSTIN`)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (`Tax invoice` screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`Test@123`)
* **Preconditions:** Delivered order `WC-01006-1` from **Coastal Beverages** has generated **`Tax invoice INV-00006`**.
* **Steps to Reproduce:**

1. Log in as `buyer3` and open **`Tax invoice INV-00006`** (Order **`WC-01006-1`**).
2. Inspect the **Seller** block in the top-left corner of the invoice.

* **Actual Result:** The top-left **Seller** block displays only the seller name **`Coastal Beverages`** and omits the seller's GSTIN (whereas the Buyer block displays `GSTIN 30AAFCB3333C1Z3`).
* **Expected Result:** Per `BR-19` and `§5.4`, the top-left **Seller** section must display both **`Coastal Beverages`** and its registered **GSTIN**.

---

### DEF-09: Invoice Grand Total Rendered in Near-Invisible Light Gray Text

* **Rule / Spec Violated:** Functional Design Document `§5.4 Invoice` (Total must be clearly displayed on the invoice) & UI contrast/accessibility
* **Severity:** `Minor`
* **Component / Layer:** Frontend / CSS (`Tax invoice` screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`Test@123`)
* **Preconditions:** Delivered order `WC-01006-1` has generated **`Tax invoice INV-00006`**.
* **Steps to Reproduce:**

1. Open **`Tax invoice INV-00006`** (Order **`WC-01006-1`**).
2. Inspect the summary footer line below the line-items table (`Net ₹11249.54 · Tax ₹2024.91 · Total ₹13274.46`).

* **Actual Result:** While `Net ₹11249.54` and `Tax ₹2024.91` are rendered in high-contrast dark text, **`Total ₹13274.46`** is styled in ultra-light gray text on a white background, making the invoice total nearly invisible to the user.
* **Expected Result:** **`Total ₹13274.46`** must be rendered in high-contrast dark text matching or exceeding the legibility of `Net` and `Tax`.

---

### DEF-10: Invoice Tax Summary Uses Per-Line Sum Instead of Summed-Rate Rounding, Causing a `₹0.01` Mismatch

* **Rule / Spec Violated:** `BR-07` (On orders and invoices, tax is calculated per tax rate on the summed net value of all lines at that rate, rounded half-up to the paisa once per rate)
* **Severity:** `Minor`
* **Component / Layer:** Backend / Invoice Calculation (`Tax invoice` screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`Test@123`)
* **Preconditions:** **`Tax invoice INV-00006`** (Order **`WC-01006-1`**) contains three `18%` tax line items: `Select Packaged Water 1L` (`Net: ₹1092.96`, `Tax: ₹196.73`), `Daily Iced Tea` (`Net: ₹4636.80`, `Tax: ₹834.62`), and `Spice Cold Coffee` (`Net: ₹5519.78`, `Tax: ₹993.56`).
* **Steps to Reproduce:**

1. Open **`Tax invoice INV-00006`** (Order **`WC-01006-1`**).
2. Compare the footer summary figures **`Net ₹11249.54`**, **`Tax ₹2024.91`**, and **`Total ₹13274.46`** against `BR-07`.

* **Actual Result:**
* `Tax` displays **`₹2024.91`** (sum of individually rounded lines: `₹196.73 + ₹834.62 + ₹993.56 = ₹2,024.91`) instead of calculating `18%` once on the summed net value (`₹11,249.54 × 0.18 = ₹2,024.9172`, rounded to **`₹2,024.92`**).
* `Total` displays **`₹13274.46`**, creating a `₹0.01` arithmetic discrepancy with the displayed sum (`₹11,249.54 + ₹2,024.91 = ₹13,274.45`).


* **Expected Result:** Per `BR-07`, `18%` tax must be calculated once on the summed net value (`₹11,249.54 × 0.18 = ₹2,024.92`), ensuring `Net ₹11,249.54 + Tax ₹2,024.92 = Total ₹13,274.46`.

---

### DEF-11: Negative-Stock Product Is Returned in Catalogue Search Results and Displays Active Add-to-Cart (`BR-02`)

* **Rule / Spec Violated:** `BR-02` (Products with no stock are never shown in the catalogue or in search results) & `BR-01`
* **Severity:** `Major`
* **Component / Layer:** Backend / Frontend (Catalogue Search & Product detail page)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Accounts: `buyer1` (`Spice Route Kitchen`) / `buyer3` (`शर्मा भोजनालय`), Password: `Test@123`
* **Preconditions:** Chef's Butter (`SKU-0293`, WholeCart Direct) has a negative inventory balance of `-61 pieces` (caused by `DEF-01`).
* **Steps to Reproduce:**

1. Log in as a buyer (`buyer1` or `buyer3`) and open **Catalogue**.
2. Observe that **Chef's Butter** (`SKU-0293`) is omitted from the default alphabetical catalogue table (between *Chef's Basmati Rice* and *Chef's Cardamom*).
3. Enter `Butter` (or `SKU-0293`) in **Search name or SKU** and click **Search**.
4. Click **Chef's Butter** in the search results to open its product detail page.

* **Actual Result:**
* **Catalogue Search** returns **Chef's Butter** (`SKU-0293`) despite its negative stock (`-61 pieces`).
* Opening the product page displays `In stock: -61 pieces` alongside an enabled **Add to cart** button.


* **Expected Result:** Per `BR-02`, products with zero or negative stock (`stock <= 0`) must never appear in **Catalogue** or **Search** results, and out-of-stock product pages must not allow adding items to the cart.
* **Evidence Files to Attach:** `DEF-11_search.jpg` (Catalogue search results showing Chef's Butter) and `DEF-11_product.jpg` (Chef's Butter page showing `In stock: -61 pieces` and active Add to cart).

---

### DEF-12: Checkout Accepts Sub-Orders Below the ₹2,000 Net Minimum Per Seller (`CR-07` / `BR-04`)

* **Rule / Spec Violated:** `CR-07` (Minimum order raised to ₹2,000 net per seller, replaces the amount in `BR-04`) & `BR-04` (The minimum order is ₹2,000 net before tax **per seller** in the checkout)
* **Severity:** `Major`
* **Component / Layer:** Backend / Checkout Validation (`POST /api/orders`)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`शर्मा भोजनालय`, `Test@123`)
* **Preconditions:** `buyer3`'s cart contains products from two sellers, each with a net value below ₹2,000.
* **Steps to Reproduce:**

1. Log in as `buyer3` and add products to the cart from two sellers such that each seller's sub-order net is below ₹2,000:
   - **FreshFarm Foods** — Net: `₹1,965.12`
   - **WholeCart Direct** — Net: `₹1,920.00`
2. Navigate to **Cart** → **Proceed to checkout**.
3. Observe the **Checkout** screen: both seller sub-orders display net values below `₹2,000` and the system proceeds without any validation error.
4. Select a valid delivery date (`2026-11-18`) and slot (`08:00–11:00`), then click **Place order**.
5. Navigate to **Orders** and confirm both sub-orders were created.

* **Actual Result:** Checkout accepts and processes both sub-orders (Grand total `₹4,584.44`) despite each seller's net value (`₹1,965.12` and `₹1,920.00`) being below the `₹2,000` minimum. Orders **WC-01010-2** (FreshFarm Foods, `₹2,318.84`) and **WC-01010-1** (WholeCart Direct, `₹2,265.60`) are successfully created.
* **Expected Result:** Per `CR-07` (which supersedes `BR-04`), checkout must reject any seller sub-order whose net value is below `₹2,000` and display a clear validation error indicating the shortfall per seller.
* **Evidence:** `DEF-12.png` (Checkout screen showing both sub-orders below ₹2,000 net), `DEF-12-ordered.png` (Order history confirming both orders placed successfully)

---

### DEF-13: Broken Access Control (IDOR) Allows a Buyer to View Another Buyer's Tax Invoice (`BR-21`)

* **Rule / Spec Violated:** `BR-21` (Buyers can only see their own orders, invoices and statements. Sellers can only see their own orders.)
* **Severity:** `Critical`
* **Component / Layer:** Backend / Frontend Authorization (Invoices screen / Invoice endpoint)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Accounts: `buyer3` (`शर्मा भोजनालय`) and `buyer1` (`Spice Route Kitchen`), Password: `Test@123`
* **Preconditions:** Buyer `buyer3` (`शर्मा भोजनालय`, GSTIN `30AAFCB3333C1Z3`) has a generated tax invoice (e.g., `INV-00006` for order `WC-01006-1`).
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`), open **Invoices**, select a tax invoice (e.g., `INV-00006`), and copy its direct URL.
2. Log out of `buyer3` and log in as a different buyer account (`buyer1` — `Spice Route Kitchen`).
3. Paste and navigate directly to `buyer3`'s invoice URL (or request the invoice ID via the API).

* **Actual Result:** The system renders `buyer3`'s complete tax invoice to `buyer1`, exposing confidential B2B customer details (business name, GSTIN, billing/delivery address), seller details, line items, pricing, and tax totals.
* **Expected Result:** Per `BR-21`, buyers must only be authorized to view their own invoices; attempting to access another buyer's invoice URL or ID must be blocked with `HTTP 403 Forbidden` or `HTTP 404 Not Found`.

---

### DEF-14: Operator Logistics CSV Export Reverses `slot_start` and `slot_end` Values (`IF-02`)

* **Rule / Spec Violated:** Integration Specification `IF-02` — Logistics export (Columns in order: `order_number`, `buyer_name`, `delivery_address`, `delivery_date`, `slot_start`, `slot_end`, `pieces`)
* **Severity:** `Major`
* **Component / Layer:** Backend / Operator Export (Logistics export / `logistics.csv`)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `operator` (`Test@123`)
* **Preconditions:** Order `WC-01007-1` (Café Dosa Corner, delivery slot `2026-11-18 11:00–14:00`) is in `accepted` status.
* **Steps to Reproduce:**

1. Confirm order `WC-01007-1` has delivery slot `2026-11-18 11:00–14:00` and status `accepted`.
2. Log in as `operator` and click **Logistics export** to download `logistics.csv`.
3. Open `logistics.csv` and inspect the `slot_start` and `slot_end` columns for row `WC-01007-1`.

* **Actual Result:** `logistics.csv` exports `slot_start = 14:00` and `slot_end = 11:00`, inverting the start and end times of the delivery window (`14:00` to `11:00`).
* **Expected Result:** For a `11:00–14:00` delivery slot, `logistics.csv` must export `slot_start = 11:00` and `slot_end = 14:00`.

---

### DEF-15: Catalogue `Case` Column Uses Misleading Multiplication Notation (`12 × ₹2759.89`) That Reads as Unit Price × Quantity

* **Rule / Spec Violated:** Functional Design Document `§5.1 Catalogue` & UI Clarity / Consistency
* **Severity:** `Minor`
* **Component / Layer:** Frontend / UI (`Catalogue` table)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer1` (`Spice Route Kitchen`, `Test@123`)
* **Preconditions:** Buyer is logged in and viewing the **Catalogue** page.
* **Steps to Reproduce:**

1. Log in as `buyer1` (`Spice Route Kitchen`) and navigate to **Catalogue**.
2. Inspect the **Piece** and **Case** columns across rows (e.g., **Chef's Basmati Rice** `SKU-0006`: `Piece: ₹249.99`, `Case: 12 × ₹2759.89`; **Chef's Cardamom** `SKU-0257`: `Piece: ₹249.99`, `Case: 24 × ₹5519.78`).
3. Compare with the product detail page format (`Case of 24: ₹2649.60`).

* **Actual Result:** The **Case** column displays `{case_size} × ₹{total_case_price}` (e.g., `12 × ₹2759.89`), which mathematically reads as `12` units priced at `₹2,759.89` each (`₹33,118.68`), even though `₹2759.89` is the discounted total price for the entire 12-piece case.
* **Expected Result:** The **Case** column should display unambiguous phrasing matching the product detail page—such as `Case of 12: ₹2,759.89` or `₹2,759.89 (12 pcs)`—so buyers do not misinterpret the case total as a per-piece price.

---

### DEF-16: Catalogue Listing Omits Seller Name and Available Stock Quantity

* **Rule / Spec Violated:** UI Usability & B2B Ordering Workflow (`BR-01` stock awareness & `BR-04`/`CR-07` per-seller minimum order)
* **Severity:** `Minor`
* **Component / Layer:** Frontend / UI (`Catalogue` table)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer1` (`Spice Route Kitchen`, `Test@123`)
* **Preconditions:** Buyer is browsing **Catalogue** to build an order meeting the `₹2,000` net per-seller minimum (`CR-07`).
* **Steps to Reproduce:**

1. Log in as `buyer1` and open **Catalogue**.
2. Inspect the table headers (`SKU`, `Product`, `Category`, `Piece`, `Case`, `Tax`).

* **Actual Result:** Neither the **Seller** name nor the **Available Stock** quantity is shown in the **Catalogue** table—forcing buyers to click into each product's detail page individually just to check who sells the SKU (`Sold by ...`) and how many pieces are in stock (`In stock: ... pieces`).
* **Expected Result:** The **Catalogue** table should include **Seller** and **Stock** columns (or badges) so buyers can identify same-seller products and check available inventory directly from the listing.

---

### DEF-17: Catalogue Search Bar Lacks a `Clear/Reset` Control and Renders Left-Aligned Numeric Prices Without Thousands Separators

* **Rule / Spec Violated:** UI Polish / Formatting Consistency (`Catalogue` screen)
* **Severity:** `Minor`
* **Component / Layer:** Frontend / CSS (`Catalogue` filter bar and table)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer1` (`Spice Route Kitchen`, `Test@123`)
* **Preconditions:** Buyer is viewing the **Catalogue** page.
* **Steps to Reproduce:**

1. Open **Catalogue** and inspect the search/filter bar (`Search name or SKU`, `All categories`, `Search`).
2. Inspect the numeric columns (`Piece`, `Case`, `Tax`) in the product table.

* **Actual Result:**
* There is no **Clear** or **Reset** button next to **Search** to quickly restore the full `547`-product list after filtering by keyword or category.
* Multi-thousand rupee values are formatted without comma grouping (e.g., `₹2759.89`, `₹5519.78`, `₹8059.20`), and numeric columns (`Piece`, `Case`, `Tax`) are left-aligned instead of right-aligned.


* **Expected Result:** Include a **Clear** / **Reset** control beside **Search**, format currency values with standard thousands separators (e.g., `₹2,759.89`), and right-align numeric price and tax columns for readability.

---
### DEF-18: System Allows Cumulative Refund/Exchange Requests Exceeding Ordered Line Quantity

* **Rule / Spec Violated:** `BR-17` (Buyers can request refunds or exchanges on delivered orders up to the quantity ordered across cumulative requests)
* **Severity:** `Major`
* **Component / Layer:** Backend / Frontend (Orders → Refund / Exchange submission)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Accounts: `buyer3` (`शर्मा भोजनालय`) and `seller1` (`WholeCart Direct`), Password: `Test@123`
* **Preconditions:** Buyer `buyer3` has a **delivered** order (e.g., `WC-01004-1` or `WC-01006-1`) containing a line item with ordered quantity $N$.
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`), open a **delivered** order, and submit a partial refund request for `1` unit of a line item.
2. Log in as the order's seller (`seller1` — `WholeCart Direct`), navigate to **Refunds**, and approve the first refund request.
3. Log back in as `buyer3`, reopen the same delivered order, and submit a **second** refund or exchange request on that same line item for quantity $N$ (so cumulative requested quantity = $1 + N > N$).

* **Actual Result:** The system accepts the second request even though the cumulative quantity across requests exceeds the quantity originally ordered.
* **Expected Result:** Per `BR-17`, the system must track previously requested/approved refund and exchange quantities per order line and reject any subsequent request where `cumulative_qty > ordered_qty`.

---
### DEF-19: Refund/Exchange Form Retains Full Ordered Quantities and Active `Send request` After Full Refund Submission

* **Rule / Spec Violated:** `BR-17` (Buyers can request refunds or exchanges on delivered orders up to the quantity ordered across cumulative requests)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (Orders → Refund or exchange section)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/orders/6](http://13.200.185.1/c/janaki-47b177/orders/6)` | Account: `buyer3` (`शर्मा भोजनालय`, `Test@123`)
* **Preconditions:** Delivered order `WC-01006-1` (Seller: **Coastal Beverages**) contains *Select Packaged Water 1L* (1 case), *Daily Iced Tea* (2 case), and *Spice Cold Coffee* (2 case).
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`) and open delivered order `WC-01006-1`.
2. In the **Refund or exchange** section, submit a full refund request for all ordered cases (1, 2, 2) and click **Send request**.
3. Inspect the page after the confirmation banner `"Request sent to the seller"` appears.

* **Actual Result:** Despite the banner confirming `"Request sent to the seller"`, the **Refund or exchange** form still displays the full ordered quantities as available—*Select Packaged Water 1L* (1 case), *Daily Iced Tea* (2 case), *Spice Cold Coffee* (2 case)—with active quantity inputs and an enabled **Send request** button, allowing the buyer to submit additional/cumulative refund or exchange requests exceeding the ordered quantities.
* **Expected Result:** Per `BR-17`, quantities already claimed in pending or approved refund/exchange requests must be subtracted from each line's remaining eligible quantity (and once 100% of ordered quantities have been requested, further requests must be blocked).

---

### DEF-20: Active `Cancel order` Button Displayed on a `delivered` Order Past the 5-Minute Window

* **Rule / Spec Violated:** `BR-11` (A buyer can cancel an order within 5 minutes of placement, before shipment) & `BR-13` (Delivered orders cannot be cancelled)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (Order details screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/orders/6](http://13.200.185.1/c/janaki-47b177/orders/6)` | Account: `buyer3` (`शर्मा भोजनालय`, `Test@123`)
* **Preconditions:** Order `WC-01006-1` was placed on `2026-11-12 02:55:12` (6 days prior) and is in `delivered` status.
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`) and open **Order WC-01006-1** (Status: delivered).
2. Inspect the action buttons displayed below the order summary (`Net ₹11249.54 · Tax ₹2024.92 · Total ₹13274.46`).

* **Actual Result:** The red **Cancel order** button is rendered and enabled on a `delivered` order placed 6 days earlier (`2026-11-12 02:55:12`).
* **Expected Result:** Per `BR-11` and `BR-13`, **Cancel order** must only be available within 5 minutes of order placement while the order is unshipped; it must never appear on `shipped` or `delivered` orders.

---

### DEF-21: `Change slot` and `Request redelivery` Controls Rendered Active on an Already `delivered` Order

* **Rule / Spec Violated:** `BR-14` (Redelivery applies only when delivery has failed) & `BR-15` (Buyers can only change delivery slots before shipment)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (Order details screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/orders/6](http://13.200.185.1/c/janaki-47b177/orders/6)` | Account: `buyer3` (`शर्मा भोजनालय`, `Test@123`)
* **Preconditions:** Order `WC-01006-1` is in `delivered` status (Delivery: `2026-11-12 11:00–14:00`).
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`) and open **Order WC-01006-1** (Status: delivered).
2. Inspect the **Change delivery slot / request redelivery** section.

* **Actual Result:** Both **Change slot** and **Request redelivery** inputs and buttons are rendered and enabled on a `delivered` order.
* **Expected Result:** Per `BR-14` and `BR-15`, **Change slot** must be hidden/disabled once an order is `shipped` or `delivered`, and **Request redelivery** must only appear when the order status is `delivery failed`.

---
### DEF-22: Order Cancelled by Seller Remains in `accepted` Status on Buyer's Order View

* **Rule / Spec Violated:** `BR-13` (Order lifecycle & seller cancellation: placed and accepted orders can be cancelled by the seller, transitioning the order to `cancelled`)
* **Severity:** `Critical`
* **Component / Layer:** Backend / Order State Synchronization (Seller Orders → Buyer Order details)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/orders/7](http://13.200.185.1/c/janaki-47b177/orders/7)` | Accounts: `seller1` (`WholeCart Direct`) and `buyer2` (`Café Dosa Corner`), Password: `Test@123`
* **Preconditions:** Order `WC-01007-1` (Café Dosa Corner, Seller: **WholeCart Direct**, Total `₹27723.47`) is in `accepted` status.
* **Steps to Reproduce:**

1. Log in as `seller1` (`WholeCart Direct`), open **Orders**, and click **Cancel** on accepted order `WC-01007-1`.
2. Log out of `seller1` and log in as `buyer2` (`Café Dosa Corner`).
3. Navigate to **Orders** and open **Order WC-01007-1** (`/orders/7`).

* **Actual Result:** On the buyer's order detail page (`/orders/7`), **Order WC-01007-1** still displays `Status: accepted` along with active buyer controls (*Cancel order*, *Change slot*, *Request redelivery*, and *Refund or exchange*) instead of reflecting the seller's cancellation.
* **Expected Result:** Per `BR-13`, cancelling an `accepted` order as the seller must immediately update the order's persisted status to `cancelled` across both seller and buyer views, disable all active order modification controls, and restore reserved inventory (`BR-11`).
* *Note:* Check whether `WC-01007-1` shows `cancelled` on the seller side (role state desync) or reverted back to `accepted` (silent backend failure on cancelling an accepted order).

---

### DEF-23: `Refund or exchange` Form Is Rendered Active on an Undelivered (`accepted`) Order

* **Rule / Spec Violated:** `BR-17` (Buyers can request refunds or exchanges on `delivered` orders up to the quantity ordered)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (Order details → Refund or exchange section)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/orders/7](http://13.200.185.1/c/janaki-47b177/orders/7)` | Account: `buyer2` (`Café Dosa Corner`, `Test@123`)
* **Preconditions:** Order `WC-01007-1` (Chef's Butter, `95 case` + `9 piece`) is in `accepted` status and has **not** been delivered.
* **Steps to Reproduce:**

1. Log in as `buyer2` (`Café Dosa Corner`) and open undelivered order `WC-01007-1` (Status: accepted).
2. Scroll to the **Refund or exchange** section at the bottom of the page.

* **Actual Result:** The entire **Refund or exchange** form (Chef's Butter (`95 case`), Chef's Butter (`9 piece`), type selector, and **Send request** button) is rendered and enabled on an `accepted` order that has not even been shipped or delivered.
* **Expected Result:** Per `BR-17`, refund and exchange requests are permitted **only** on `delivered` orders; the **Refund or exchange** section must not appear on `placed`, `accepted`, `shipped`, or `cancelled` orders.

---
### DEF-24: `Cancel order` Button Remains Enabled After Order Status Is Already `cancelled`

* **Rule / Spec Violated:** `BR-11` (Buyer cancellation applies only to unshipped active orders within 5 minutes of placement) & `BR-13` (Order lifecycle — `cancelled` is a terminal state with no further cancellation action)
* **Severity:** `Major`
* **Component / Layer:** Frontend / Backend (Order details screen)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer2` (`Café Dosa Corner`, `Test@123`)
* **Preconditions:** Buyer `buyer2` (`Café Dosa Corner`) placed order `WC-01011-2` (Chef's Basmati Rice, 1 case, Seller: **FreshFarm Foods**, Total `₹3015.88`) at `2026-11-18 09:40:20` and cancelled it.
* **Steps to Reproduce:**

1. Log in as `buyer2` (`Café Dosa Corner`) and place an order (`WC-01011-2`).
2. Open **Order WC-01011-2** and click **Cancel order** to cancel the order.
3. Inspect the **Order WC-01011-2** detail page after the status updates to `Status: cancelled`.

* **Actual Result:** Even though the order header clearly displays `Status: cancelled`, the red **Cancel order** button remains rendered and enabled on the page (alongside *Change slot*, *Request redelivery*, and *Refund or exchange*).
* **Expected Result:** Per `BR-11` and `BR-13`, once an order transitions to `cancelled`, the **Cancel order** button (as well as slot-change, redelivery, and refund/exchange forms) must be hidden or disabled.

---
### DEF-25: Clicking `Change slot` on a `cancelled` Order Displays Misleading Shipped-Order Error Banner

* **Rule / Spec Violated:** `BR-13` (Terminal `cancelled` order state) & `BR-15` (Delivery slot modifications apply only to active unshipped orders) (Related to `DEF-21`)
* **Severity:** `Minor`
* **Component / Layer:** Frontend / Backend (Order details → Change delivery slot)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer2` (`Café Dosa Corner`, `Test@123`)
* **Preconditions:** Order `WC-01011-2` (Delivery: `2026-11-18 14:00–17:00`) is in `cancelled` status.
* **Steps to Reproduce:**

1. Log in as `buyer2` (`Café Dosa Corner`) and open **Order WC-01011-2** (Status: `cancelled`).
2. In the **Change delivery slot / request redelivery** section, select a slot (`08:00–11:00`) and click **Change slot**.
3. Inspect the validation banner displayed at the top of the page.

* **Actual Result:** The system leaves **Change slot** enabled on a `cancelled` order and responds with the misleading error banner `"The delivery slot can only be changed before the order ships"`, falsely implying the order is blocked because it has already shipped rather than because it is `cancelled`.
* **Expected Result:** Per `BR-13` and `BR-15`, slot-modification controls must be hidden/disabled on `cancelled` orders (and if called via API, return an accurate state error indicating the order is already `cancelled`).

---
### DEF-26: `Request redelivery` Control Remains Enabled on a `cancelled` Order

* **Rule / Spec Violated:** `BR-14` (Redelivery applies only when delivery has failed) & `BR-13` (Order lifecycle — `cancelled` is a terminal state)
* **Severity:** `Minor` (Backend rejects submission with `"Redelivery is only available after a failed delivery"`, so impact is UI-only)
* **Component / Layer:** Frontend / UI (Order details → Change delivery slot / request redelivery)
* **Environment:** `[http://13.200.185.1/c/janaki-47b177/](http://13.200.185.1/c/janaki-47b177/)` | Account: `buyer3` (`शर्मा भोजनालय`, `Test@123`)
* **Preconditions:** Order `WC-01009-2` (Chef's Cold Coffee, 4 case, Seller: **FreshFarm Foods**, Total `₹2318.84`) is in `cancelled` status.
* **Steps to Reproduce:**

1. Log in as `buyer3` (`शर्मा भोजनालय`) and open **Order WC-01009-2** (Status: `cancelled`).
2. Inspect the **Change delivery slot / request redelivery** section and click **Request redelivery**.

* **Actual Result:** The date picker, slot dropdown, and **Request redelivery** button are rendered active on a `cancelled` order; clicking **Request redelivery** triggers the validation error banner `"Redelivery is only available after a failed delivery"`.
* **Expected Result:** Per `BR-13` and `BR-14`, **Request redelivery** (along with *Change slot*, *Cancel order*, and *Refund or exchange*) must be hidden or disabled whenever an order is in `cancelled` status.

---