using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace CurrencyConverter;

public partial class MainPage : ContentPage
{
    private static readonly HttpClient Client = new();
    private const int MaxHistoryEntries = 20;
    private static readonly Dictionary<string, CurrencyDetails> CurrencyCatalog = BuildCurrencyCatalog();
    private static readonly Dictionary<string, string> CurrencyRegionOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EUR"] = "eu", ["USD"] = "us", ["GBP"] = "gb", ["CAD"] = "ca", ["AUD"] = "au",
        ["NZD"] = "nz", ["CHF"] = "ch", ["JPY"] = "jp", ["CNY"] = "cn", ["INR"] = "in",
        ["KRW"] = "kr", ["RUB"] = "ru", ["BRL"] = "br", ["MXN"] = "mx", ["ZAR"] = "za",
        ["TRY"] = "tr", ["SGD"] = "sg", ["HKD"] = "hk", ["SEK"] = "se", ["NOK"] = "no",
        ["DKK"] = "dk", ["PLN"] = "pl", ["CZK"] = "cz", ["HUF"] = "hu", ["THB"] = "th",
        ["IDR"] = "id", ["MYR"] = "my", ["PHP"] = "ph", ["AED"] = "ae", ["SAR"] = "sa",
        ["ILS"] = "il", ["TWD"] = "tw", ["PKR"] = "pk", ["EGP"] = "eg", ["KWD"] = "kw",
        ["QAR"] = "qa", ["UAH"] = "ua", ["ARS"] = "ar", ["CLP"] = "cl", ["COP"] = "co",
        ["PEN"] = "pe", ["NGN"] = "ng", ["KES"] = "ke", ["GHS"] = "gh", ["MAD"] = "ma",
        ["XOF"] = "sn", ["XAF"] = "cm"
    };

    private readonly Dictionary<string, decimal> rates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ConversionHistoryEntry> historyEntries = [];
    private List<CurrencyOption> availableCurrencyOptions = [];
    private CancellationTokenSource? historyCancellation;
    private ConversionHistoryEntry? pendingHistoryEntry;
    private CurrencyOption? selectedFrom;
    private CurrencyOption? selectedTo;
    private bool selectingFrom;
    private bool sanitizingAmount;
    private bool ratesLoaded;
    private bool hasLoadedRates;
    private bool restoringHistory;

    public ObservableCollection<ConversionHistoryEntry> HistoryEntries => historyEntries;

    public sealed record ConversionHistoryEntry(decimal Amount, string From, string To, decimal ConvertedAmount)
    {
        public string DisplayText => $"{DateTime.Now.ToString("g", CultureInfo.CurrentCulture)}  " +
                                     $"{Amount.ToString("N2", CultureInfo.CurrentCulture)} {From} → " +
                                     $"{ConvertedAmount.ToString("N2", CultureInfo.CurrentCulture)} {To}";
    }

    private sealed record CurrencyDetails(string Name, string Region);

    private sealed record CurrencyOption(string Code, string Name, string Region)
    {
        public string Label => $"{Code} — {Name}";
        public string? FlagImageUrl => Region.Length == 2
            ? $"https://flagcdn.com/w80/{Region.ToLowerInvariant()}.png"
            : null;
    }

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (hasLoadedRates)
        {
            return;
        }

        hasLoadedRates = true;
        await LoadRatesAsync();
    }

    private static Dictionary<string, CurrencyDetails> BuildCurrencyCatalog()
    {
        var catalog = new Dictionary<string, CurrencyDetails>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                     .OrderBy(culture => culture.Name, StringComparer.Ordinal))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                string code = region.ISOCurrencySymbol;
                string name = region.CurrencyEnglishName;
                if (code.Length == 3 && code != "XXX" && !string.IsNullOrWhiteSpace(name) && !catalog.ContainsKey(code))
                {
                    catalog[code] = new CurrencyDetails(name, region.TwoLetterISORegionName);
                }
            }
            catch (ArgumentException)
            {
                // Some specific cultures do not map to a currency region.
            }
        }

        return catalog;
    }

    private async Task LoadRatesAsync()
    {
        try
        {
            using var response = await Client.GetAsync("https://open.er-api.com/v6/latest/EUR");
            response.EnsureSuccessStatusCode();

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("result", out JsonElement result) || result.GetString() != "success" ||
                !root.TryGetProperty("rates", out JsonElement ratesElement))
            {
                throw new HttpRequestException("The exchange-rate service returned an invalid response.");
            }

            rates.Clear();
            foreach (JsonProperty rate in ratesElement.EnumerateObject())
            {
                if (rate.Value.TryGetDecimal(out decimal value) && value > 0)
                {
                    rates[rate.Name] = value;
                }
            }

            if (!rates.ContainsKey("EUR"))
            {
                rates["EUR"] = 1m;
            }

            availableCurrencyOptions = rates.Keys
                .OrderBy(code => code, StringComparer.Ordinal)
                .Select(CreateCurrencyOption)
                .ToList();
            currencyOptionsList.ItemsSource = availableCurrencyOptions;
            selectedFrom = availableCurrencyOptions.FirstOrDefault(option => option.Code == "EUR");
            selectedTo = availableCurrencyOptions.FirstOrDefault(option => option.Code == "USD") ??
                         availableCurrencyOptions.FirstOrDefault(option => option.Code == "EUR");
            UpdateSelectedCurrencyFlags();
            ratesLoaded = true;
            await CalculateResult(recordHistory: false);

            if (root.TryGetProperty("time_last_update_utc", out JsonElement updatedElement) &&
                DateTimeOffset.TryParse(updatedElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out DateTimeOffset updated))
            {
                txtUpdated.Text = $"Rates last updated: {updated.ToLocalTime():g}";
            }
            else
            {
                txtUpdated.Text = "Exchange rates loaded.";
            }

            await ShowToastSafe("✅ Exchange rates loaded!", ToastDuration.Short);
        }
        catch (Exception)
        {
            txtResult.Text = "Rates unavailable";
            txtUpdated.Text = "Check your internet connection and try again.";
            await ShowToastSafe("⚠️ Could not load exchange rates", ToastDuration.Long);
        }
    }

    private static async Task ShowToastSafe(string message, ToastDuration duration)
    {
        try
        {
            await Toast.Make(message, duration, 14).Show();
        }
        catch
        {
            // Platform toasts may fail on unpackaged Windows apps where AUMID / push notifications are not configured
        }
    }

    private static CurrencyOption CreateCurrencyOption(string code)
    {
        CurrencyCatalog.TryGetValue(code, out CurrencyDetails? details);
        string region = CurrencyRegionOverrides.TryGetValue(code, out string? overrideRegion)
            ? overrideRegion
            : details?.Region ?? string.Empty;
        return new CurrencyOption(code, details?.Name ?? code, region);
    }

    private void InputChanged(object? sender, TextChangedEventArgs e)
    {
        if (sanitizingAmount || sender is not Entry entry || e.NewTextValue is null)
        {
            if (!sanitizingAmount)
            {
                _ = CalculateResult();
            }

            return;
        }

        string sanitized = SanitizeAmount(e.NewTextValue);
        if (!string.Equals(sanitized, e.NewTextValue, StringComparison.Ordinal))
        {
            int cursorPosition = Math.Clamp(entry.CursorPosition, 0, e.NewTextValue.Length);
            int sanitizedCursorPosition = SanitizeAmount(e.NewTextValue[..cursorPosition]).Length;
            sanitizingAmount = true;
            entry.Text = sanitized;
            entry.CursorPosition = Math.Min(sanitizedCursorPosition, sanitized.Length);
            sanitizingAmount = false;
        }

        _ = CalculateResult();
    }

    private static string SanitizeAmount(string value)
    {
        char decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
        bool hasDecimalSeparator = false;
        var sanitized = new System.Text.StringBuilder(value.Length);

        foreach (char character in value)
        {
            if (character is >= '0' and <= '9')
            {
                sanitized.Append(character);
            }
            else if ((character == '.' || character == ',') && !hasDecimalSeparator)
            {
                sanitized.Append(decimalSeparator);
                hasDecimalSeparator = true;
            }
        }

        return sanitized.ToString();
    }

    private void CurrencySearchChanged(object? sender, TextChangedEventArgs e)
    {
        string searchText = e.NewTextValue?.Trim() ?? string.Empty;
        currencyOptionsList.ItemsSource = string.IsNullOrEmpty(searchText)
            ? availableCurrencyOptions
            : availableCurrencyOptions.Where(option =>
                option.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                option.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void OpenFromCurrencySelector(object? sender, TappedEventArgs e)
    {
        selectingFrom = true;
        currencySearch.Text = string.Empty;
        currencySelectorOverlay.IsVisible = true;
    }

    private void OpenToCurrencySelector(object? sender, TappedEventArgs e)
    {
        selectingFrom = false;
        currencySearch.Text = string.Empty;
        currencySelectorOverlay.IsVisible = true;
    }

    private void CloseCurrencySelector(object? sender, EventArgs e)
    {
        currencySelectorOverlay.IsVisible = false;
        currencySearch.Text = string.Empty;
        currencyOptionsList.SelectedItem = null;
    }

    private void CurrencyOptionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CurrencyOption option)
        {
            return;
        }

        if (selectingFrom)
        {
            selectedFrom = option;
        }
        else
        {
            selectedTo = option;
        }

        UpdateSelectedCurrencyFlags();
        CloseCurrencySelector(sender, EventArgs.Empty);
        _ = CalculateResult();
    }

    private void UpdateSelectedCurrencyFlags()
    {
        txtFromCurrency.Text = selectedFrom?.Label ?? "Select currency";
        imgFromFlag.Source = selectedFrom?.FlagImageUrl;
        txtToCurrency.Text = selectedTo?.Label ?? "Select currency";
        imgToFlag.Source = selectedTo?.FlagImageUrl;
    }

    private void SetQuickAmount(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string amount })
        {
            txtAmount.Text = amount;
        }
    }

    private void SwapCurrencies(object? sender, EventArgs e)
    {
        if (selectedFrom is not CurrencyOption from || selectedTo is not CurrencyOption to)
        {
            return;
        }

        selectedFrom = to;
        selectedTo = from;
        UpdateSelectedCurrencyFlags();
        _ = CalculateResult();
    }

    private void HistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ConversionHistoryEntry entry)
        {
            RestoreHistoryEntry(entry);
            lstHistory.SelectedItem = null;
        }
    }

    private void RestoreHistoryEntry(ConversionHistoryEntry entry)
    {
        CancelPendingHistory();
        restoringHistory = true;
        try
        {
            txtAmount.Text = entry.Amount.ToString(CultureInfo.CurrentCulture);
            selectedFrom = currencyOptionsList.ItemsSource is IEnumerable<CurrencyOption> currencies
                ? currencies.FirstOrDefault(currency => currency.Code == entry.From)
                : null;
            selectedTo = currencyOptionsList.ItemsSource is IEnumerable<CurrencyOption> toCurrencies
                ? toCurrencies.FirstOrDefault(currency => currency.Code == entry.To)
                : null;
            UpdateSelectedCurrencyFlags();
        }
        finally
        {
            restoringHistory = false;
        }

        _ = CalculateResult(recordHistory: false);
    }

    private async Task CalculateResult(bool recordHistory = true)
    {
        if (!ratesLoaded || selectedFrom is not CurrencyOption from ||
            selectedTo is not CurrencyOption to)
        {
            return;
        }

        string amountText = string.Concat(txtAmount.Text.Where(character => !char.IsWhiteSpace(character)));
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amount) || amount < 0)
        {
            txtResult.Text = "Enter a valid amount.";
            CancelPendingHistory();
            return;
        }

        if (!rates.TryGetValue(from.Code, out decimal fromRate) || !rates.TryGetValue(to.Code, out decimal toRate))
        {
            txtResult.Text = "Rates unavailable";
            txtRate.Text = string.Empty;
            CancelPendingHistory();
            return;
        }

        txtRate.Text = $"1 {from.Code} = {(toRate / fromRate).ToString("N4", CultureInfo.CurrentCulture)} {to.Code}";

        decimal convertedAmount;
        try
        {
            convertedAmount = amount / fromRate * toRate;
        }
        catch (OverflowException)
        {
            txtResult.Text = "Amount is too large.";
            CancelPendingHistory();
            return;
        }

        txtResult.Text = $"{convertedAmount.ToString("N2", CultureInfo.CurrentCulture)} {to.Code}";

        // Subtle scale animation on result update
        await txtResult.ScaleToAsync(1.05, 100, Easing.CubicOut);
        await txtResult.ScaleToAsync(1.0, 100, Easing.CubicIn);

        if (recordHistory && !restoringHistory)
        {
            ScheduleHistoryEntry(new ConversionHistoryEntry(amount, from.Code, to.Code, convertedAmount));
        }
    }

    private void ScheduleHistoryEntry(ConversionHistoryEntry entry)
    {
        CancelPendingHistory();
        pendingHistoryEntry = entry;
        historyCancellation = new CancellationTokenSource();
        _ = AddHistoryEntryAfterDelayAsync(entry, historyCancellation.Token);
    }

    private async Task AddHistoryEntryAfterDelayAsync(ConversionHistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
            if (pendingHistoryEntry != entry)
            {
                return;
            }

            historyEntries.Insert(0, entry);
            pendingHistoryEntry = null;
            while (historyEntries.Count > MaxHistoryEntries)
            {
                historyEntries.RemoveAt(historyEntries.Count - 1);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer input superseded this pending history entry.
        }
    }

    private void CancelPendingHistory()
    {
        historyCancellation?.Cancel();
        historyCancellation?.Dispose();
        historyCancellation = null;
        pendingHistoryEntry = null;
    }
}