using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MogLocker;

namespace MogLocker.AdBlock
{
    public sealed class AdBlockPanel : UserControl
    {
        private readonly AdBlockStore store;
        private readonly FilterListService filterService;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly CheckBox enabledCheckBox;
        private readonly Label blockedCountLabel;
        private readonly Label statsBreakdownLabel;
        private readonly Label statusLabel;
        private readonly DataGridView recentGrid;
        private readonly CheckedListBox filterListBox;
        private readonly TextBox userRuleTextBox;
        private readonly ListBox userRuleListBox;
        private readonly TextBox allowlistTextBox;
        private readonly ListBox allowlistListBox;
        private readonly Button updateButton;
        private bool isRefreshing;
        private bool initialized;
        private DateTime lastAutoUpdateCheckUtc = DateTime.MinValue;
        private int compiledSummaryRevision = -1;
        private RuleCompileResult compiledSummary;

        public AdBlockPanel()
        {
            store = new AdBlockStore();
            filterService = new FilterListService(store);
            BackColor = System.Drawing.Color.FromArgb(12, 17, 31);
            ForeColor = System.Drawing.Color.FromArgb(226, 232, 244);
            Dock = DockStyle.Fill;
            Padding = new Padding(16);

            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(4)
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = System.Drawing.Color.Transparent,
                Padding = new Padding(12, 2, 12, 2),
                Margin = new Padding(4, 0, 4, 0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            var heading = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = System.Drawing.Color.Transparent,
                Margin = new Padding(0)
            };
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            var pageTitle = MakeLabel("AdBlock", true);
            pageTitle.AutoSize = false;
            pageTitle.Dock = DockStyle.Fill;
            pageTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 20F,
                System.Drawing.FontStyle.Bold);
            pageTitle.ForeColor = System.Drawing.Color.FromArgb(237, 244, 255);
            pageTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            pageTitle.Margin = new Padding(0);
            var pageSubtitle = MakeLabel("Браузерная защита от рекламы и трекеров", false);
            pageSubtitle.AutoSize = false;
            pageSubtitle.Dock = DockStyle.Fill;
            pageSubtitle.ForeColor = System.Drawing.Color.FromArgb(142, 160, 185);
            pageSubtitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            pageSubtitle.Margin = new Padding(1, 0, 0, 2);
            heading.Controls.Add(pageTitle, 0, 0);
            heading.Controls.Add(pageSubtitle, 0, 1);
            enabledCheckBox = new CheckBox
            {
                Text = "AdBlock включён",
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold),
                ForeColor = ForeColor,
                Anchor = AnchorStyles.Left,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(12, 4, 4, 4)
            };
            enabledCheckBox.CheckedChanged += AdBlockEnabledChanged;
            var countCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(19, 39, 48),
                Margin = new Padding(8, 7, 2, 7),
                Padding = new Padding(10, 0, 10, 0)
            };
            blockedCountLabel = MakeLabel("0 заблокировано", true);
            blockedCountLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 9.5F,
                System.Drawing.FontStyle.Bold);
            blockedCountLabel.ForeColor = System.Drawing.Color.FromArgb(104, 211, 184);
            blockedCountLabel.Dock = DockStyle.Fill;
            blockedCountLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            countCard.Controls.Add(blockedCountLabel);
            header.Controls.Add(heading, 0, 0);
            header.Controls.Add(countCard, 1, 0);

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = System.Drawing.Color.FromArgb(18, 26, 45),
                Padding = new Padding(10, 6, 10, 6),
                Margin = new Padding(4, 2, 4, 5)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145F));
            statusLabel = MakeLabel("Загрузка настроек AdBlock…", false);
            statusLabel.AutoEllipsis = true;
            var bridgeButton = MakeButton("Подключить браузер", 136);
            bridgeButton.Click += RegisterBridge;
            updateButton = MakeButton("Обновить фильтры", 140);
            updateButton.Click += RefreshFiltersClicked;
            var clearButton = MakeButton("Очистить статистику", 140);
            clearButton.Click += ClearStatisticsClicked;
            toolbar.Controls.Add(enabledCheckBox, 0, 0);
            toolbar.Controls.Add(bridgeButton, 2, 0);
            toolbar.Controls.Add(updateButton, 3, 0);
            toolbar.Controls.Add(clearButton, 4, 0);

            var statusStrip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(15, 23, 38),
                Padding = new Padding(10, 3, 10, 3),
                Margin = new Padding(4, 0, 4, 4)
            };
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            statusLabel.Margin = new Padding(0);
            statusLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            statusStrip.Controls.Add(statusLabel);

            var recentGroup = MakeGroup("Последние заблокированные запросы");
            statsBreakdownLabel = MakeLabel("Домены и типы ресурсов появятся после первых блокировок.", false);
            statsBreakdownLabel.Dock = DockStyle.Top;
            statsBreakdownLabel.AutoEllipsis = true;
            statsBreakdownLabel.Height = 24;
            recentGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.Color.FromArgb(12, 17, 31),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                GridColor = System.Drawing.Color.FromArgb(43, 56, 79)
            };
            recentGrid.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(24, 35, 56);
            recentGrid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
            recentGrid.DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(18, 26, 45);
            recentGrid.DefaultCellStyle.ForeColor = ForeColor;
            recentGrid.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(41, 65, 88);
            recentGrid.DefaultCellStyle.SelectionForeColor = ForeColor;
            recentGrid.Columns.Add("time", "Время");
            recentGrid.Columns.Add("domain", "Домен");
            recentGrid.Columns.Add("type", "Тип");
            recentGrid.Columns.Add("url", "Запрос");
            recentGrid.Columns["time"].FillWeight = 18;
            recentGrid.Columns["domain"].FillWeight = 25;
            recentGrid.Columns["type"].FillWeight = 17;
            recentGrid.Columns["url"].FillWeight = 40;
            recentGroup.Controls.Add(recentGrid);
            recentGroup.Controls.Add(statsBreakdownLabel);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = BackColor,
                Margin = new Padding(4, 4, 4, 4)
            };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 29F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 36F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));

            var filtersGroup = MakeGroup("Активные фильтр-листы");
            filterListBox = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.Color.FromArgb(18, 26, 45),
                ForeColor = ForeColor,
                CheckOnClick = true
            };
            filterListBox.ItemCheck += FilterListItemCheck;
            filtersGroup.Controls.Add(filterListBox);

            var rulesGroup = MakeGroup("Пользовательские правила");
            var rulesLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = System.Drawing.Color.FromArgb(18, 26, 45),
                Padding = new Padding(8)
            };
            rulesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            rulesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rulesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            var addRuleRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = System.Drawing.Color.Transparent,
                Margin = new Padding(0)
            };
            userRuleTextBox = MakeTextBox("||ads.example^$script");
            userRuleTextBox.Width = 185;
            var addRuleButton = MakeButton("Добавить", 90);
            addRuleButton.Click += AddRuleClicked;
            addRuleRow.Controls.Add(userRuleTextBox);
            addRuleRow.Controls.Add(addRuleButton);
            userRuleListBox = MakeListBox();
            var ruleButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = System.Drawing.Color.Transparent,
                Margin = new Padding(0)
            };
            var removeRuleButton = MakeButton("Удалить", 82);
            removeRuleButton.Click += RemoveRuleClicked;
            var importButton = MakeButton("Импорт", 82);
            importButton.Click += ImportRulesClicked;
            var exportButton = MakeButton("Экспорт", 82);
            exportButton.Click += ExportRulesClicked;
            ruleButtons.Controls.Add(removeRuleButton);
            ruleButtons.Controls.Add(importButton);
            ruleButtons.Controls.Add(exportButton);
            rulesLayout.Controls.Add(addRuleRow, 0, 0);
            rulesLayout.Controls.Add(userRuleListBox, 0, 1);
            rulesLayout.Controls.Add(ruleButtons, 0, 2);
            rulesGroup.Controls.Add(rulesLayout);

            var allowGroup = MakeGroup("Allowlist — сайты без блокировки");
            var allowLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = System.Drawing.Color.FromArgb(18, 26, 45),
                Padding = new Padding(8)
            };
            allowLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            allowLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var addAllowRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                BackColor = System.Drawing.Color.Transparent,
                Margin = new Padding(0)
            };
            allowlistTextBox = MakeTextBox("example.com");
            allowlistTextBox.Width = 185;
            var addAllowButton = MakeButton("Добавить", 90);
            addAllowButton.Click += AddAllowlistClicked;
            var removeAllowButton = MakeButton("Удалить", 90);
            removeAllowButton.Click += RemoveAllowlistClicked;
            allowlistListBox = MakeListBox();
            addAllowRow.Controls.Add(allowlistTextBox);
            addAllowRow.Controls.Add(addAllowButton);
            addAllowRow.Controls.Add(removeAllowButton);
            allowLayout.Controls.Add(addAllowRow, 0, 0);
            allowLayout.Controls.Add(allowlistListBox, 0, 1);
            allowGroup.Controls.Add(allowLayout);

            right.Controls.Add(filtersGroup, 0, 0);
            right.Controls.Add(rulesGroup, 0, 1);
            right.Controls.Add(allowGroup, 0, 2);

            page.Controls.Add(header, 0, 0);
            page.SetColumnSpan(header, 2);
            page.Controls.Add(toolbar, 0, 1);
            page.SetColumnSpan(toolbar, 2);
            page.Controls.Add(statusStrip, 0, 2);
            page.SetColumnSpan(statusStrip, 2);
            page.Controls.Add(recentGroup, 0, 3);
            page.Controls.Add(right, 1, 3);
            Controls.Add(page);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            refreshTimer.Tick += RefreshTimerTick;
            Load += delegate { Initialize(); };
            Disposed += delegate
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
            };
        }

        public async void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            LoadState();
            refreshTimer.Start();
            await TryAutomaticUpdateAsync();
        }

        private void RefreshTimerTick(object sender, EventArgs e)
        {
            LoadState();
            RunAutomaticUpdate();
        }

        private async void RunAutomaticUpdate()
        {
            await TryAutomaticUpdateAsync();
        }

        private async Task TryAutomaticUpdateAsync()
        {
            if (isRefreshing || DateTime.UtcNow - lastAutoUpdateCheckUtc < TimeSpan.FromHours(1))
                return;
            lastAutoUpdateCheckUtc = DateTime.UtcNow;
            try
            {
                AdBlockState state = store.Read();
                DateTime attempted = state.LastUpdateAttemptUtc ?? DateTime.MinValue;
                if (!state.AutoUpdate || DateTime.UtcNow - attempted < TimeSpan.FromHours(24))
                    return;
                await RefreshFiltersAsync(true);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Не удалось проверить обновление фильтров: " + ex.Message;
                MessageBox.Show(this, statusLabel.Text, "Ошибка AdBlock",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadState()
        {
            try
            {
                AdBlockState state = store.Read();
                if (enabledCheckBox.Checked != state.Enabled)
                    enabledCheckBox.Checked = state.Enabled;
                blockedCountLabel.Text = state.BlockedCount.ToString("N0") + " заблокировано";
                string domains = String.Join(", ", state.DomainCounts
                    .OrderByDescending(item => item.Value).Take(3)
                    .Select(item => item.Key + " (" + item.Value.ToString("N0") + ")"));
                string types = String.Join(", ", state.ResourceTypeCounts
                    .OrderByDescending(item => item.Value).Take(4)
                    .Select(item => item.Key + " " + item.Value.ToString("N0")));
                statsBreakdownLabel.Text = "Домены: " + (domains.Length == 0 ? "—" : domains) +
                    "  |  Типы: " + (types.Length == 0 ? "—" : types);
                UpdateFilterList(state.FilterLists);
                UpdateStringList(userRuleListBox, state.UserRules);
                UpdateStringList(allowlistListBox, state.Allowlist);
                UpdateRecentGrid(state.RecentBlocked);
                string bridgeState = NativeMessagingRegistration.IsRegisteredForCurrentUser()
                    ? "Мост зарегистрирован"
                    : "Браузер не подключён";
                string lastUpdate = state.LastUpdatedUtc.HasValue
                    ? "  ·  Фильтры: " + state.LastUpdatedUtc.Value.ToLocalTime().ToString("g")
                    : "  ·  Фильтр-листы ещё не загружались";
                if (compiledSummary == null || compiledSummaryRevision != state.FilterRevision)
                {
                    compiledSummary = new FilterRuleEngine(store).Compile(state);
                    compiledSummaryRevision = state.FilterRevision;
                }
                string ruleSummary = state.Enabled
                    ? String.Format("  ·  {0} сетевых / {1} CSS правил  ·  пропущено {2}",
                        compiledSummary.NetworkRules.Count, compiledSummary.CosmeticRules.Count,
                        compiledSummary.UnsupportedRuleCount)
                    : "  ·  Фильтрация выключена";
                statusLabel.Text = bridgeState + lastUpdate + " " + ruleSummary;
                statusLabel.AutoEllipsis = true;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка чтения настроек AdBlock: " + ex.Message;
            }
        }

        private void UpdateFilterList(IList<FilterListSetting> lists)
        {
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FilterListSetting list in lists)
            {
                current.Add(list.Id);
                int index = -1;
                for (int i = 0; i < filterListBox.Items.Count; i++)
                {
                    FilterListSetting item = filterListBox.Items[i] as FilterListSetting;
                    if (item != null && item.Id == list.Id)
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                {
                    filterListBox.Items.Add(list, list.Enabled);
                }
                else
                {
                    filterListBox.Items[index] = list;
                    if (filterListBox.GetItemChecked(index) != list.Enabled)
                        filterListBox.SetItemChecked(index, list.Enabled);
                }
            }

            for (int i = filterListBox.Items.Count - 1; i >= 0; i--)
            {
                FilterListSetting item = filterListBox.Items[i] as FilterListSetting;
                if (item == null || !current.Contains(item.Id))
                    filterListBox.Items.RemoveAt(i);
            }
        }

        private static void UpdateStringList(ListBox listBox, IEnumerable<string> values)
        {
            string[] current = listBox.Items.Cast<object>().Select(item => Convert.ToString(item)).ToArray();
            string[] next = values.ToArray();
            if (current.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
                return;
            listBox.BeginUpdate();
            listBox.Items.Clear();
            listBox.Items.AddRange(next);
            listBox.EndUpdate();
        }

        private void UpdateRecentGrid(IList<BlockedRequest> requests)
        {
            recentGrid.SuspendLayout();
            recentGrid.Rows.Clear();
            foreach (BlockedRequest request in requests.Take(200))
            {
                int index = recentGrid.Rows.Add(
                    request.TimeUtc.ToLocalTime().ToString("HH:mm:ss"),
                    request.Domain,
                    request.ResourceType,
                    request.Url);
                recentGrid.Rows[index].Cells["url"].ToolTipText = request.Url;
            }
            recentGrid.ResumeLayout();
        }

        private void AdBlockEnabledChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
                return;
            try
            {
                store.Update(state => state.Enabled = enabledCheckBox.Checked, true);
                statusLabel.Text = enabledCheckBox.Checked
                    ? "AdBlock включён. Расширение применит изменения не позднее чем через минуту."
                    : "AdBlock выключен. Расширение снимет динамические правила не позднее чем через минуту.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить настройку AdBlock: " + ex.Message,
                    "Ошибка настроек", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadState();
            }
        }

        private void FilterListItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.Index < 0 || e.Index >= filterListBox.Items.Count)
                return;
            var list = filterListBox.Items[e.Index] as FilterListSetting;
            if (list == null)
                return;
            bool enabled = e.NewValue == CheckState.Checked;
            BeginInvoke(new Action(delegate
            {
                try
                {
                    store.Update(state =>
                    {
                        FilterListSetting current = state.FilterLists.FirstOrDefault(item => item.Id == list.Id);
                        if (current != null)
                            current.Enabled = enabled;
                    }, true);
                    statusLabel.Text = "Состояние фильтра сохранено; расширение обновится не позднее чем через минуту.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось изменить фильтр-лист: " + ex.Message,
                        "Ошибка фильтра", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    LoadState();
                }
            }));
        }

        private void AddRuleClicked(object sender, EventArgs e)
        {
            string rule = userRuleTextBox.Text.Trim();
            string error;
            if (!FilterRuleEngine.ValidateUserRule(rule, out error))
            {
                MessageBox.Show(this, error, "Неподдерживаемое правило",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (store.Read().UserRules.Count >= FilterRuleEngine.MaximumUserRules)
                {
                    MessageBox.Show(this, "Можно хранить не более 500 пользовательских правил.",
                        "Достигнут лимит", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                store.Update(state =>
                {
                    if (!state.UserRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
                        state.UserRules.Add(rule);
                }, true);
                userRuleTextBox.Clear();
                LoadState();
                statusLabel.Text = "Правило сохранено и будет применено расширением браузера.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить правило: " + ex.Message,
                    "Ошибка правила", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveRuleClicked(object sender, EventArgs e)
        {
            string rule = Convert.ToString(userRuleListBox.SelectedItem);
            if (String.IsNullOrEmpty(rule))
                return;
            try
            {
                store.Update(state => state.UserRules.RemoveAll(
                    item => String.Equals(item, rule, StringComparison.OrdinalIgnoreCase)), true);
                LoadState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось удалить правило: " + ex.Message,
                    "Ошибка правила", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddAllowlistClicked(object sender, EventArgs e)
        {
            List<string> domains = FilterRuleEngine.NormalizeDomains(allowlistTextBox.Text);
            if (domains.Count == 0)
            {
                MessageBox.Show(this, "Введите домен или URL сайта, например example.com.",
                    "Неверный домен", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string domain = domains[0];
            try
            {
                store.Update(state =>
                {
                    if (!state.Allowlist.Contains(domain, StringComparer.OrdinalIgnoreCase))
                    {
                        if (state.Allowlist.Count >= FilterRuleEngine.MaximumAllowlistDomains)
                            throw new InvalidDataException("Можно добавить не более 200 сайтов в Allowlist.");
                        state.Allowlist.Add(domain);
                    }
                }, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Не удалось добавить исключение",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            allowlistTextBox.Clear();
            LoadState();
        }

        private void RemoveAllowlistClicked(object sender, EventArgs e)
        {
            string domain = Convert.ToString(allowlistListBox.SelectedItem);
            if (String.IsNullOrEmpty(domain))
                return;
            try
            {
                store.Update(state => state.Allowlist.RemoveAll(
                    item => String.Equals(item, domain, StringComparison.OrdinalIgnoreCase)), true);
                LoadState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось удалить исключение: " + ex.Message,
                    "Ошибка Allowlist", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void RefreshFiltersClicked(object sender, EventArgs e)
        {
            await RefreshFiltersAsync(false);
        }

        private async Task RefreshFiltersAsync(bool automatic)
        {
            if (isRefreshing)
                return;
            isRefreshing = true;
            updateButton.Enabled = false;
            statusLabel.Text = automatic
                ? "Автоматическое обновление фильтр-листов…"
                : "Загружаю фильтр-листы…";
            try
            {
                FilterRefreshResult result = await filterService.RefreshAsync(CancellationToken.None);
                statusLabel.Text = result.Message;
                if (result.FailedCount > 0)
                    MessageBox.Show(this, result.Message, "Часть фильтров не обновлена",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Не удалось обновить фильтры: " + ex.Message;
                MessageBox.Show(this, statusLabel.Text, "Ошибка обновления фильтров",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isRefreshing = false;
                updateButton.Enabled = true;
                LoadState();
            }
        }

        private void ClearStatisticsClicked(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Очистить журнал и статистику заблокированных запросов?",
                "Очистить статистику", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                store.Update(state =>
                {
                    state.BlockedCount = 0;
                    state.DomainCounts.Clear();
                    state.ResourceTypeCounts.Clear();
                    state.RecentBlocked.Clear();
                });
                LoadState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось очистить статистику: " + ex.Message,
                    "Ошибка статистики", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportRulesClicked(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Импорт пользовательских правил",
                Filter = "Текстовые правила (*.txt)|*.txt|Все файлы (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    string[] imported = File.ReadAllLines(dialog.FileName)
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0 && !line.StartsWith("!", StringComparison.Ordinal))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (imported.Length > FilterRuleEngine.MaximumUserRules)
                        throw new InvalidDataException("В файле больше 500 правил.");
                    for (int index = 0; index < imported.Length; index++)
                    {
                        string error;
                        if (!FilterRuleEngine.ValidateUserRule(imported[index], out error))
                            throw new InvalidDataException("Строка " + (index + 1) + ": " + error);
                    }
                    store.Update(state =>
                    {
                        foreach (string rule in imported)
                        {
                            if (!state.UserRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
                                state.UserRules.Add(rule);
                        }
                    });
                    LoadState();
                    statusLabel.Text = "Импортировано правил: " + imported.Length + ".";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Импорт не выполнен: " + ex.Message,
                        "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportRulesClicked(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Экспорт пользовательских правил",
                Filter = "Текстовые правила (*.txt)|*.txt",
                FileName = "moglocker-user-rules.txt",
                AddExtension = true,
                DefaultExt = "txt"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    File.WriteAllLines(dialog.FileName, store.Read().UserRules);
                    statusLabel.Text = "Пользовательские правила экспортированы.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Экспорт не выполнен: " + ex.Message,
                        "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RegisterBridge(object sender, EventArgs e)
        {
            string extensionDirectory = NativeMessagingRegistration.ExtensionDirectory;
            string message = "Будет зарегистрирован локальный Native Messaging host для Chrome и Edge в HKCU. " +
                "Это нужно расширению, чтобы применять ваши фильтры и показывать статистику.\r\n\r\n" +
                "Затем откройте страницу расширений браузера, включите режим разработчика и загрузите папку:\r\n" +
                extensionDirectory + "\r\n\r\nПродолжить?";
            if (MessageBox.Show(this, message, "Подключение расширения AdBlock",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                NativeMessagingRegistration.RegisterForCurrentUser();
                LoadState();
                MessageBox.Show(this,
                    "Мост зарегистрирован. В Edge откройте edge://extensions, в Chrome — chrome://extensions, " +
                    "включите «Режим разработчика» и выберите «Загрузить распакованное расширение».",
                    "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Не удалось подключить браузер: " + ex.Message;
                MessageBox.Show(this, statusLabel.Text, "Ошибка подключения",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static GroupBox MakeGroup(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.FromArgb(183, 199, 222),
                BackColor = System.Drawing.Color.FromArgb(17, 25, 42),
                Padding = new Padding(10),
                Margin = new Padding(5)
            };
        }

        private static Label MakeLabel(string text, bool bold)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = System.Drawing.Color.FromArgb(226, 232, 244),
                Font = new System.Drawing.Font("Segoe UI", bold ? 10F : 8.5F,
                    bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular),
                Margin = new Padding(4, 12, 4, 4)
            };
        }

        private static AnimatedButton MakeButton(string text, int width)
        {
            var button = new AnimatedButton
            {
                Text = text,
                Width = width,
                Height = 30,
                BackColor = System.Drawing.Color.FromArgb(35, 49, 70),
                ForeColor = System.Drawing.Color.FromArgb(225, 233, 246),
                Font = new System.Drawing.Font("Segoe UI Semibold", 8.5F,
                    System.Drawing.FontStyle.Bold),
                Margin = new Padding(3, 3, 5, 3)
            };
            button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(56, 76, 101);
            button.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(46, 66, 89);
            return button;
        }

        private static TextBox MakeTextBox(string placeholder)
        {
            return new TextBox
            {
                Text = String.Empty,
                AccessibleDescription = placeholder,
                BackColor = System.Drawing.Color.FromArgb(12, 17, 31),
                ForeColor = System.Drawing.Color.FromArgb(226, 232, 244),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new System.Drawing.Font("Segoe UI", 9F),
                Margin = new Padding(3, 5, 5, 3)
            };
        }

        private static ListBox MakeListBox()
        {
            return new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(12, 17, 31),
                ForeColor = System.Drawing.Color.FromArgb(226, 232, 244),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
        }
    }
}
