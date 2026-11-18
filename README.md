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

## Running Automation Tests

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Playwright browsers

### Setup & Run
```bash
cd automation/WholeCart.Automation

# Restore packages
dotnet restore

# Install Playwright browsers
dotnet build
pwsh bin/Debug/net8.0/playwright.ps1 install chromium

# Run tests
dotnet test
```

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

**Password for all accounts:** `Test@123`

## AI Tools Used

Gemini AI for documentation
