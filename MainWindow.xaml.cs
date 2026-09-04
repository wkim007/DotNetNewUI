using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow : Window
{
    private int _zoom = 100;
    private readonly Dictionary<string, ObservableCollection<ColumnInfo>> _columns = new()
    {
        ["Customer"] = [new("customer_id", "INT", true), new("first_name", "VARCHAR", true), new("last_name", "VARCHAR", true), new("email", "VARCHAR", true)],
        ["Order"] = [new("order_id", "INT", true), new("customer_id", "INT", true), new("order_date", "DATETIME", true), new("status", "VARCHAR", true)],
        ["OrderItem"] = [new("order_item_id", "INT", true), new("order_id", "INT", true), new("product_id", "INT", true), new("quantity", "INT", true)],
        ["Product"] = [new("product_id", "INT", true), new("category_id", "INT", true), new("name", "VARCHAR", true), new("unit_price", "DECIMAL", true)]
    };

    public MainWindow()
    {
        InitializeComponent();
        SelectEntity("Order");
    }

    private void SelectEntity(string key)
    {
        var display = key == "OrderItem" ? "Order Item" : key;
        SelectedEntityTitle.Text = display.ToUpperInvariant();
        EntityNameBox.Text = display;
        PhysicalNameBox.Text = key switch { "OrderItem" => "order_item", "Order" => "sales_order", _ => key.ToLowerInvariant() };
        ColumnsGrid.ItemsSource = _columns.GetValueOrDefault(key, []);
        StatusText.Text = $"Selected entity: {display}";
    }

    private void Entity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string key }) SelectEntity(key);
    }

    private void ModelTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string key }) SelectEntity(key);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Min(160, _zoom + 10));
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Max(50, _zoom - 10));
    private void SetZoom(int value)
    {
        _zoom = value;
        DiagramView.LayoutTransform = new ScaleTransform(_zoom / 100d, _zoom / 100d);
        ZoomLabel.Text = $"{_zoom}%";
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Validation complete — no errors or warnings";
        MessageBox.Show("Model validation completed successfully.\n\n5 entities checked\n4 relationships checked\n0 issues found", "Validate Model", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        const string ddl = "CREATE TABLE sales_order (\n  order_id INT NOT NULL PRIMARY KEY,\n  customer_id INT NOT NULL,\n  order_date DATETIME2 NOT NULL,\n  status VARCHAR(20) NOT NULL\n);";
        Clipboard.SetText(ddl);
        StatusText.Text = "DDL generated and copied to clipboard";
        MessageBox.Show(ddl + "\n\nCopied to clipboard.", "Generated SQL Server DDL", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NewEntity_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Entity creation workflow is ready for your implementation.", "New Entity", MessageBoxButton.OK, MessageBoxImage.Information);
    private void AddRelationship_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Relationship tool active — choose parent and child entities";
    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        if (ColumnsGrid.ItemsSource is ObservableCollection<ColumnInfo> items) items.Add(new("new_column", "VARCHAR", false));
    }
    private void EntityNameBox_LostFocus(object sender, RoutedEventArgs e) => SelectedEntityTitle.Text = EntityNameBox.Text.ToUpperInvariant();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (EmptyHint is null) return;
        EmptyHint.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text) || new[] { "customer", "order", "item", "product", "category" }.Any(x => x.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "Ready" : $"Filtering objects by ‘{SearchBox.Text}’";
    }
}

public sealed record ColumnInfo(string Name, string Type, bool Required);
