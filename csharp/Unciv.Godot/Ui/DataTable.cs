using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>A column of a <see cref="DataTable"/>: the untranslated header, whether its cells are numbers (right-aligned, tabular, sorted by value) and, when not 0, the width in pixels its cells keep (C077: a long entry is not cut to an ellipsis).</summary>
public readonly record struct DataColumn(string Header, bool Numeric = false, int MinWidth = 0);

/// <summary>
/// C065: a table in an Inset well. The header row is clickable (first click sorts ascending, the next descending, shown by a triangle), numbers are
/// right-aligned in tabular figures and rows alternate Surface1 and Surface2. The order comes from <see cref="TableSorter"/>; the rows keep their input order when equal.
/// C074: with <c>actions</c> the table has one more, header-less column at the right that holds a control per row (a "go to" icon button, a row's Move and Coup buttons);
/// <see cref="SetRows"/> takes the factory, which is called once per input row, so the control stays the same object when the table is sorted.
/// C092: with <c>multiSelect</c> a click on a row toggles it in <see cref="SelectedRows"/> (shown in the accent colour) instead of choosing the one <see cref="SelectedRow"/>.
/// </summary>
public partial class DataTable : PanelContainer
{
    private readonly IReadOnlyList<DataColumn> columns;
    private readonly GridContainer grid = new();
    private readonly List<Button> headers = [];
    private readonly List<List<Label>> cells = [];
    private IReadOnlyList<IReadOnlyList<string>> rows = [];
    private IReadOnlyList<int> order = [];
    private readonly bool hasActions;
    private readonly bool selectable;
    private readonly bool multiSelect;
    private readonly SortedSet<int> selectedRows = [];
    private IReadOnlyList<Control?> actionControls = [];

    public DataTable(IReadOnlyList<DataColumn> columns, bool actions = false, bool selectable = false, bool multiSelect = false)
    {
        this.columns = columns;
        hasActions = actions;
        this.multiSelect = multiSelect;
        this.selectable = selectable || multiSelect;
        ThemeTypeVariation = UiVariants.PanelInset;
        grid.Columns = Math.Max(1, columns.Count + (actions ? 1 : 0));
        grid.AddThemeConstantOverride("h_separation", 0);
        grid.AddThemeConstantOverride("v_separation", 0);
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(grid);
        Rebuild();
    }

    /// <summary>C075: the selected input row (a selectable table; a click on any cell of a row selects it), or -1.</summary>
    public int SelectedRow { get; private set; } = -1;

    /// <summary>C092: the toggled input rows of a <c>multiSelect</c> table, in input order.</summary>
    public IReadOnlyCollection<int> SelectedRows => selectedRows;

    /// <summary>C092: <see cref="SelectedRows"/> changed (by a click, <see cref="Toggle"/> or <see cref="SetSelectedRows"/> with notify).</summary>
    public event Action? SelectionChanged;

    /// <summary>C075: a row was selected by a click or by <see cref="Select"/>.</summary>
    public event Action<int>? RowSelected;

    /// <summary>C075: the colour of the text of an input row (null for the default); set before <see cref="SetRows"/>. The selected row reads in the accent colour.</summary>
    public Func<int, Color?>? RowTone { get; set; }

    /// <summary>The sorted column, or -1 while the rows are in input order.</summary>
    public int SortColumn { get; private set; } = -1;

    public bool SortAscending { get; private set; } = true;

    public int RowCount => rows.Count;

    /// <summary>The input row indices in display order.</summary>
    public IReadOnlyList<int> Order => order;

    /// <summary>The header button of a column (press it to sort).</summary>
    public Button HeaderButton(int column) => headers[column];

    /// <summary>The label of a cell by display position.</summary>
    public Label Cell(int displayRow, int column) => cells[displayRow][column];

    /// <summary>The text of a cell by display position.</summary>
    public string CellText(int displayRow, int column) => cells[displayRow][column].Text;

    /// <summary>The action control of an input row (null when the row has none or the table has no action column).</summary>
    public Control? RowAction(int inputRow) => inputRow >= 0 && inputRow < actionControls.Count ? actionControls[inputRow] : null;

    /// <summary>Replaces the rows (each with one cell per column; missing cells are empty) and keeps the current sort. <paramref name="rowAction"/> makes the control of the action column for an input row.</summary>
    public void SetRows(IReadOnlyList<IReadOnlyList<string>> newRows, Func<int, Control?>? rowAction = null)
    {
        ReleaseActions();
        foreach (var control in actionControls) control?.QueueFree();
        rows = newRows;
        selectedRows.RemoveWhere(it => it >= newRows.Count);
        actionControls = hasActions && rowAction != null ? Enumerable.Range(0, newRows.Count).Select(rowAction).ToList() : [];
        Rebuild();
    }

    /// <summary>Takes the action controls out of their cells so that freeing the cells does not free them.</summary>
    private void ReleaseActions()
    {
        foreach (var control in actionControls) control?.GetParent()?.RemoveChild(control);
    }

    /// <summary>C075: selects an input row (-1 clears the selection) and raises <see cref="RowSelected"/> for a valid one unless <paramref name="notify"/> is false.</summary>
    public void Select(int inputRow, bool notify = true)
    {
        if (inputRow >= rows.Count) inputRow = -1;
        SelectedRow = inputRow;
        Rebuild();
        if (notify && inputRow >= 0) RowSelected?.Invoke(inputRow);
    }

    /// <summary>C092: toggles an input row of a <c>multiSelect</c> table.</summary>
    public void Toggle(int inputRow)
    {
        if (inputRow < 0 || inputRow >= rows.Count) return;
        if (!selectedRows.Remove(inputRow)) selectedRows.Add(inputRow);
        Rebuild();
        SelectionChanged?.Invoke();
    }

    /// <summary>C092: replaces the selection by the given input rows (rows outside the table are ignored).</summary>
    public void SetSelectedRows(IEnumerable<int> inputRows, bool notify = false)
    {
        selectedRows.Clear();
        foreach (int row in inputRows.Where(it => it >= 0 && it < rows.Count)) selectedRows.Add(row);
        Rebuild();
        if (notify) SelectionChanged?.Invoke();
    }

    /// <summary>Sorts by a column in the given direction.</summary>
    public void SortBy(int column, bool ascending)
    {
        if (column < 0 || column >= columns.Count) return;
        SortColumn = column;
        SortAscending = ascending;
        Rebuild();
    }

    private void OnHeaderPressed(int column) => SortBy(column, SortColumn != column || !SortAscending);

    private void Rebuild()
    {
        ReleaseActions();
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }
        headers.Clear();
        cells.Clear();
        for (int c = 0; c < columns.Count; c++)
        {
            int column = c;
            string arrow = SortColumn != c ? "" : SortAscending ? " ▲" : " ▼";
            var header = new Button
            {
                Text = columns[c].Header.TrText() + arrow,
                ThemeTypeVariation = UiVariants.ButtonGhost,
                Alignment = columns[c].Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            header.Pressed += () => OnHeaderPressed(column);
            headers.Add(header);
            grid.AddChild(header);
        }
        if (hasActions) grid.AddChild(new Control());
        order = SortColumn < 0 ? Enumerable.Range(0, rows.Count).ToList() : TableSorter.Order(rows, SortColumn, SortAscending);
        for (int r = 0; r < order.Count; r++)
        {
            var line = new List<Label>();
            var row = rows[order[r]];
            for (int c = 0; c < columns.Count; c++)
            {
                var label = new Label
                {
                    Text = c < row.Count ? row[c] : "",
                    HorizontalAlignment = columns[c].Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                if (columns[c].MinWidth > 0) label.CustomMinimumSize = new Vector2(columns[c].MinWidth, 0f);
                if (columns[c].Numeric) label.ThemeTypeVariation = UiVariants.LabelNumber;
                int inputRow = order[r];
                if (inputRow == SelectedRow || selectedRows.Contains(inputRow)) label.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.Accent));
                else if (RowTone?.Invoke(inputRow) is { } tone) label.AddThemeColorOverride("font_color", tone);
                var cell = new PanelContainer { ThemeTypeVariation = r % 2 == 0 ? UiVariants.PanelCellEven : UiVariants.PanelCellOdd, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = selectable ? MouseFilterEnum.Pass : MouseFilterEnum.Stop };
                if (selectable)
                {
                    cell.GuiInput += input =>
                    {
                        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Callable.From(() => { if (multiSelect) Toggle(inputRow); else Select(inputRow); }).CallDeferred(); // rebuilding frees the cell whose event is being handled, so the selection runs after it
                    };
                }
                cell.AddChild(label);
                grid.AddChild(cell);
                line.Add(label);
            }
            if (hasActions)
            {
                var cell = new PanelContainer { ThemeTypeVariation = r % 2 == 0 ? UiVariants.PanelCellEven : UiVariants.PanelCellOdd };
                if (RowAction(order[r]) is { } action) cell.AddChild(action);
                grid.AddChild(cell);
            }
            cells.Add(line);
        }
    }
}
