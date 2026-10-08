# Currency Converter

A clean, desktop currency-conversion app built with **.NET MAUI**. Pick a currency pair, enter an amount, and see the conversion using the latest rates provided by the exchange-rate service.

<p align="center">
  <strong>Convert • Compare • Keep track</strong>
</p>

## Features

- Convert between currencies supported by the exchange-rate feed
- Search currencies by code or name
- Swap the source and destination currencies in one tap
- Use quick amount buttons or enter a custom amount
- See the exchange rate and when rates were last updated
- Revisit up to 20 recent conversions during the current app session
- View currency flags when available

## Platform and technology

- **Target:** Windows (`net10.0-windows10.0.19041.0`)
- **Framework:** .NET MAUI, .NET 10
- **UI toolkit:** .NET MAUI Community Toolkit
- **Exchange rates:** [open.er-api.com](https://open.er-api.com/)
- **Flags:** [FlagCDN](https://flagcdn.com/)

Rates and flag images require an internet connection. Exchange rates are retrieved when the app opens; the app does not provide financial advice.

## Run the app

### Requirements

- Windows 10 or later
- Visual Studio 2026 with the **.NET MAUI** workload, or the .NET 10 SDK with the MAUI workload installed

### Using Visual Studio

1. Open `CurrencyConverter.sln`.
2. Select the `CurrencyConverter` project and a Windows run target.
3. Build and run the app.

### Using the .NET CLI

From the repository root:

```powershell
dotnet workload restore .\CurrencyConverter\CurrencyConverter.csproj
dotnet run --project .\CurrencyConverter\CurrencyConverter.csproj --framework net10.0-windows10.0.19041.0
```

## Project layout

```text
CurrencyConverter.sln
CurrencyConverter/
├── MainPage.xaml          # Converter interface
├── MainPage.xaml.cs       # Rate loading and conversion behavior
├── MauiProgram.cs         # MAUI app configuration
├── Platforms/             # Platform-specific entry points and settings
└── Resources/             # Icons, splash screen, fonts, and styles
```

## Notes

- The app currently targets Windows; Android and iOS target frameworks are not enabled in the project file.
- Recent conversion history is kept in memory and is cleared when the app process ends.
