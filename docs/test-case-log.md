# WholeCart Automated Test Case Log

The suite contains 12 automated cases: 9 browser end-to-end tests, 1 browser smoke test, and 2 API end-to-end tests. The [README](../README.md#round-two--end-to-end-order-flow) explains how to run the tests and save evidence.

## UI tests

| Test case | Accounts | Expected result |
|---|---|---|
| `TestBuyerLoginAndNavigation` | `buyer1` | The buyer signs in and opens the catalogue. |
| `MultiSellerCheckoutCreatesTwoOrdersThatCanBeDeliveredAndInvoiced` | `buyer1`, `seller2`, `seller3` | One checkout creates two seller sub-orders. Each seller accepts, ships, and delivers its sub-order. The buyer sees the status changes and matching invoice totals. |
| `BuyerChecksOutWithPiecesAndCasesOfTheSameProduct` | `buyer1` | The cart and one placed order preserve separate lines for pieces and cases. Checkout totals include both lines, and stock drops by the combined piece count. |
| `SellerCancellationOfAcceptedOrderAppearsInBuyerHistory` | `buyer2`, `seller2` | After the seller cancels an accepted order, the buyer sees Cancelled in both history and order details, and stock returns to its pre-order piece count. |
| `CheckoutRejectsOneSellerBelowMinimumEvenWhenCartTotalQualifies` | `buyer3` | Checkout explains the ₹2,000 per-seller minimum and creates no sub-orders when one seller falls below it, even though the combined cart qualifies. |
| `BuyerCannotChangeDeliverySlotAfterSellerShipsOrder` | `buyer1`, `seller3` | The buyer cannot change the delivery slot after shipment. Both roles still see the original slot and Shipped status. |
| `BuyerCancelsNewOrderAndStockReturnsToItsOriginalPieceCount` | `buyer2` | Checkout deducts the ordered pieces. Immediate buyer cancellation restores the original stock count and updates the order status. |
| `AcceptedOrderAppearsInOperatorLogisticsExport` | `buyer2`, `seller2`, `operator` | The downloaded CSV contains the accepted order with the required columns, buyer name, delivery date, slot start and end, and piece count. |
| `OperatorNavigationIsVisibleOnlyToTheOperator` | `operator`, `buyer1`, `seller2` | The operator can open the seller list and sees the logistics export control. The buyer and seller sessions omit both controls. |
| `WholeCartDirectReshipsAndDeliversAfterBuyerRequestsRedelivery` | `buyer1`, `seller1` | After delivery fails, the buyer requests a different slot. Both roles see it, and the seller reships and delivers the order. |

## API tests

| Test case | Accounts | Expected result |
|---|---|---|
| `BuyerChecksOutAndCancelsItsOwnOrderViaApi` | `buyer2` | Checkout creates one order with the selected slot and clears the cart. Cancelling the order saves the Cancelled status. |
| `CheckoutApiRejectsCartWhenOnlyOneSellerMissesTheMinimum` | `buyer1` | Checkout rejects the request when one seller is below ₹2,000 and leaves order history unchanged. |
