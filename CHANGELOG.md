# Changelog

## 0.2.0 (unreleased)

- Added: Saudi Arabia support via `PayriffOptions.Region = Region.Saudi`. It uses
  `https://api.gccpay.sa` and defaults the currency to `SAR`. Azerbaijan (`AZN`) stays the default.
- Removed: direct (host-to-host) card payments: `Payments.DirectPayAsync()`, `DirectPayRequest`,
  `CardData` and the `CardEncryptionKey` option. Use the hosted payment page (`Orders.CreateAsync()`) instead.
- Removed `PayriffClient.ProductionUrl`; use `Region.Azerbaijan.BaseUrl()`.
- `PayriffOptions.BaseUrl` is now optional and the request `Currency` properties are nullable
  (`null` means the region default).

## 0.1.2 (2026-10-08)

- First release on NuGet.

## 0.1.1 (2026-10-07)

- NuGet package icon and repository links.

## 0.1.0 (2026-10-07)

- Initial release: orders, direct and saved-card payments, saved cards, transactions, payouts,
  v2 invoices, order callback parsing.