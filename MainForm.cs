using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImaraBankATM;

public class MainForm : Form, IMessageFilter
{
    // =========================================================================
    // CHANGE YOUR BACKGROUND IMAGE IN CODE HERE:
    // Put your image inside the 'Assets' folder and match the file name below.
    // =========================================================================
    private const string BackgroundImagePath = @"Assets\background.jpg";

    // 60-Second Inactivity Auto Sign-Out Settings
    private const int InactivityTimeoutSeconds = 60;
    private int _remainingIdleSeconds = InactivityTimeoutSeconds;
    private System.Windows.Forms.Timer _inactivityTimer = null!;

    private BankAccount? _currentAccount;
    private int _failedPinAttempts = 0;

    // Top-level Views
    private Panel _heroScreen = null!;
    private Panel _centerLoginCard = null!;
    private Panel _atmServiceScreen = null!;

    // Centered Hero PIN Controls
    private TextBox _txtPinInput = null!;
    private Label _lblPinFeedback = null!;

    // ATM Service Header Controls
    private Label _lblHeaderBank = null!;
    private Label _lblHeaderSlogan = null!;
    private Label _lblTopCustomerName = null!;
    private Label _lblTopAccountNumber = null!;
    private Button _btnEjectCard = null!;

    // Responsive Layout Containers & Lists
    private TableLayoutPanel _bodyGrid = null!;
    private Panel _menuCard = null!;
    private Label _lblMenuHeader = null!;
    private Label _lblDepSub = null!;
    private Label _lblWitSub = null!;
    private readonly List<Button> _menuButtons = new();
    private readonly List<Button> _quickAmountButtons = new();

    // ATM Service Panels
    private Panel _serviceStage = null!;
    private Panel _panelBalanceInquiry = null!;
    private Panel _panelDeposit = null!;
    private Panel _panelWithdraw = null!;
    private Panel _panelTransfer = null!;
    private Panel _panelChangePin = null!;

    // Balance Inquiry Labels
    private Label _lblBalHolderName = null!;
    private Label _lblBalAccountNo = null!;
    private Label _lblBigBalance = null!;

    // Transaction Inputs
    private TextBox _txtDepositAmount = null!;
    private TextBox _txtWithdrawAmount = null!;
    private TextBox _txtTransferRecipientAcc = null!;
    private Label _lblVerifiedRecipientName = null!;
    private TextBox _txtTransferAmount = null!;
    private TextBox _txtCurrentPin = null!;
    private TextBox _txtNewPin = null!;
    private TextBox _txtConfirmNewPin = null!;

    // Bright Bank Colors
    private readonly Color _brandEmerald = Color.FromArgb(0, 107, 63);
    private readonly Color _brandBrightGreen = Color.FromArgb(16, 150, 85);
    private readonly Color _brandGold = Color.FromArgb(212, 148, 15);
    private readonly Color _headerGold = Color.FromArgb(250, 190, 60);
    private readonly Color _brightBg = Color.FromArgb(240, 247, 243);
    private readonly Color _cardWhite = Color.White;
    private readonly Color _textDark = Color.FromArgb(22, 35, 42);
    private readonly Color _textMuted = Color.FromArgb(85, 102, 112);

    public MainForm()
    {
        DatabaseManager.InitializeDatabase();
        ConfigureWindow();
        InitializeInactivityTimer();
        BuildCenteredHeroPinScreen();
        BuildAtmServiceScreen();

        Controls.Add(_heroScreen);
        Controls.Add(_atmServiceScreen);

        // Register global mouse/keyboard listener to detect user activity across all controls
        Application.AddMessageFilter(this);

        LoadCustomBackgroundFromCode();
        ShowHeroPinScreen();

        Resize += (s, e) => ApplyResponsiveScaling();
        Shown += (s, e) => ApplyResponsiveScaling();
    }

    private void ConfigureWindow()
    {
        Text = "Imara Bank Kenya (IBK) - 24/7 Smart ATM Terminal";
        Size = new Size(1060, 700);
        MinimumSize = new Size(920, 620);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _brightBg;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular);
    }

    // =========================================================================
    // 60-SECOND INACTIVITY AUTO SIGN-OUT ENGINE
    // =========================================================================
    private void InitializeInactivityTimer()
    {
        _inactivityTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000 // Ticks every 1 second
        };
        _inactivityTimer.Tick += InactivityTimer_Tick;
    }

    public bool PreFilterMessage(ref Message m)
    {
        // Detect Mouse Move (0x0200), Left/Right Clicks (0x0201, 0x0204), or Key Presses (0x0100)
        if (_currentAccount != null &&
            (m.Msg == 0x0200 || m.Msg == 0x0201 || m.Msg == 0x0204 || m.Msg == 0x0100))
        {
            ResetInactivityCountdown();
        }
        return false;
    }

    private void ResetInactivityCountdown()
    {
        _remainingIdleSeconds = InactivityTimeoutSeconds;
        if (_btnEjectCard != null && _currentAccount != null)
        {
            _btnEjectCard.Text = $"EJECT CARD ({_remainingIdleSeconds}s)";
        }
    }

    private void InactivityTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentAccount == null)
        {
            _inactivityTimer.Stop();
            return;
        }

        _remainingIdleSeconds--;
        _btnEjectCard.Text = $"EJECT CARD ({_remainingIdleSeconds}s)";

        if (_remainingIdleSeconds <= 0)
        {
            _inactivityTimer.Stop();
            string timedOutAccount = _currentAccount.AccountNumber;
            DatabaseManager.LogSecurityEvent("AUTO SIGN-OUT (60S INACTIVITY)", timedOutAccount);
            ShowHeroPinScreen();
            MessageBox.Show(
                "For your security, your ATM session has been automatically signed out due to 60 seconds of inactivity.\n\nPlease re-enter your 4-digit PIN to continue.",
                "Session Timed Out (60s Inactivity)",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }

    private void LoadCustomBackgroundFromCode()
    {
        string[] pathsToTry =
        {
            Path.Combine(Directory.GetCurrentDirectory(), BackgroundImagePath),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BackgroundImagePath)
        };

        foreach (string path in pathsToTry)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    _heroScreen.BackgroundImage = Image.FromStream(fs);
                    _heroScreen.BackgroundImageLayout = ImageLayout.Stretch;
                    break;
                }
                catch
                {
                    // Fallback to bright emerald background if image fails
                }
            }
        }
    }

    // =========================================================================
    // 1. SINGLE CENTERED HOME SCREEN (BANK NAME + HEADLINE + 4-DIGIT PIN)
    // =========================================================================
    private void BuildCenteredHeroPinScreen()
    {
        _heroScreen = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(222, 238, 229)
        };

        _centerLoginCard = new Panel
        {
            Size = new Size(540, 540),
            BackColor = _cardWhite,
            BorderStyle = BorderStyle.FixedSingle
        };

        _heroScreen.Resize += (s, e) => CenterLoginCardOnScreen();

        TableLayoutPanel cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0)
        };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 240f));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Row 0: Bank Name Top Header
        Panel topEmeraldBanner = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _brandEmerald,
            Margin = new Padding(0)
        };
        Label lblBankName = new Label
        {
            Text = "IMARA BANK KENYA   |   24/7 SMART ATM",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Panel goldUnderline = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 4,
            BackColor = _headerGold
        };
        topEmeraldBanner.Controls.Add(lblBankName);
        topEmeraldBanner.Controls.Add(goldUnderline);

        // Row 1: Bold Bank Headline
        Panel headlinePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 251, 248),
            Padding = new Padding(10, 4, 10, 4)
        };
        Label lblKaribu = new Label
        {
            Text = "Karibu Imara Bank.",
            ForeColor = _brandEmerald,
            Font = new Font("Georgia", 19f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 42,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Label lblPamoja = new Label
        {
            Text = "Pamoja Tunajenga.",
            ForeColor = _brandGold,
            Font = new Font("Georgia", 16f, FontStyle.Bold | FontStyle.Italic),
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleCenter
        };
        headlinePanel.Controls.Add(lblPamoja);
        headlinePanel.Controls.Add(lblKaribu);

        // Row 2: Prompt
        Label lblPrompt = new Label
        {
            Text = "PLEASE ENTER YOUR 4-DIGIT ATM PIN TO CONTINUE",
            ForeColor = _textDark,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomCenter
        };

        // Row 3: PIN Box Container
        Panel pinBoxContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(120, 6, 120, 6)
        };
        _txtPinInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 19f, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 4,
            PasswordChar = '●',
            BackColor = Color.FromArgb(238, 247, 242),
            ForeColor = _brandEmerald,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtPinInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) ValidatePinAndLogin();
        };
        pinBoxContainer.Controls.Add(_txtPinInput);

        // Row 4: Centered ATM Keypad
        Panel keypadWrapper = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(95, 4, 95, 4)
        };
        TableLayoutPanel keypad = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4
        };
        for (int c = 0; c < 3; c++) keypad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int r = 0; r < 4; r++) keypad.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLEAR", "0", "ENTER" };
        foreach (string key in keys)
        {
            Button btnKey = new Button
            {
                Text = key,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };

            if (key == "CLEAR")
            {
                btnKey.BackColor = Color.FromArgb(253, 237, 236);
                btnKey.ForeColor = Color.FromArgb(192, 57, 43);
                btnKey.FlatAppearance.BorderColor = Color.FromArgb(230, 176, 170);
                btnKey.Click += (s, e) => _txtPinInput.Clear();
            }
            else if (key == "ENTER")
            {
                btnKey.BackColor = _brandBrightGreen;
                btnKey.ForeColor = Color.White;
                btnKey.FlatAppearance.BorderSize = 0;
                btnKey.Click += (s, e) => ValidatePinAndLogin();
            }
            else
            {
                btnKey.BackColor = Color.FromArgb(244, 248, 246);
                btnKey.ForeColor = _textDark;
                btnKey.FlatAppearance.BorderColor = Color.FromArgb(195, 215, 205);
                string digit = key;
                btnKey.Click += (s, e) =>
                {
                    if (_txtPinInput.Text.Length < 4)
                        _txtPinInput.Text += digit;
                };
            }
            keypad.Controls.Add(btnKey);
        }
        keypadWrapper.Controls.Add(keypad);

        // Row 5: Status / Error Feedback
        _lblPinFeedback = new Label
        {
            Text = "Use the keypad or your keyboard to key in your 4-digit PIN.",
            ForeColor = _textMuted,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        cardLayout.Controls.Add(topEmeraldBanner, 0, 0);
        cardLayout.Controls.Add(headlinePanel, 0, 1);
        cardLayout.Controls.Add(lblPrompt, 0, 2);
        cardLayout.Controls.Add(pinBoxContainer, 0, 3);
        cardLayout.Controls.Add(keypadWrapper, 0, 4);
        cardLayout.Controls.Add(_lblPinFeedback, 0, 5);

        _centerLoginCard.Controls.Add(cardLayout);
        _heroScreen.Controls.Add(_centerLoginCard);
    }

    private void CenterLoginCardOnScreen()
    {
        if (_heroScreen == null || _centerLoginCard == null) return;
        _centerLoginCard.Location = new Point(
            Math.Max(10, (_heroScreen.ClientSize.Width - _centerLoginCard.Width) / 2),
            Math.Max(10, (_heroScreen.ClientSize.Height - _centerLoginCard.Height) / 2)
        );
    }

    // =========================================================================
    // 2. MAIN ATM WORKSPACE (TALL HEADER + BREATHING ROOM + WIDE MENU)
    // =========================================================================
    private void BuildAtmServiceScreen()
    {
        _atmServiceScreen = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _brightBg
        };

        TableLayoutPanel masterScreenGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = _brightBg
        };
        masterScreenGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        masterScreenGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 116f));
        masterScreenGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        masterScreenGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Top Header Bar (34% Left / 46% Center / 20% Right keeps slogan & customer name uncut)
        TableLayoutPanel headerBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = _brandEmerald,
            Margin = new Padding(0),
            Padding = new Padding(16, 12, 16, 12)
        };
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        headerBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Header Left: Bank Brand & Slogan
        TableLayoutPanel brandStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 4)
        };
        brandStack.RowStyles.Add(new RowStyle(SizeType.Percent, 52f));
        brandStack.RowStyles.Add(new RowStyle(SizeType.Percent, 48f));

        _lblHeaderBank = new Label
        {
            Text = "IMARA BANK KENYA",
            ForeColor = _headerGold,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblHeaderSlogan = new Label
        {
            Text = "Karibu Imara Bank. Pamoja Tunajenga.",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        brandStack.Controls.Add(_lblHeaderBank, 0, 0);
        brandStack.Controls.Add(_lblHeaderSlogan, 0, 1);

        // Header Center: Account Holder Name & Account Number
        TableLayoutPanel customerStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(0, 82, 48),
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(4, 2, 4, 2)
        };
        customerStack.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        customerStack.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        _lblTopCustomerName = new Label
        {
            Text = "ACCOUNT HOLDER: -",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblTopAccountNumber = new Label
        {
            Text = "ACCOUNT NO: -",
            ForeColor = _headerGold,
            Font = new Font("Consolas", 11f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        customerStack.Controls.Add(_lblTopCustomerName, 0, 0);
        customerStack.Controls.Add(_lblTopAccountNumber, 0, 1);

        // Header Right: Eject Card Button (Displays Live 60s Inactivity Countdown)
        Panel logoutBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 0, 12) };
        _btnEjectCard = new Button
        {
            Text = "EJECT CARD (60s)",
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(211, 47, 47),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnEjectCard.FlatAppearance.BorderSize = 0;
        _btnEjectCard.Click += (s, e) => ShowHeroPinScreen();
        logoutBox.Controls.Add(_btnEjectCard);

        headerBar.Controls.Add(brandStack, 0, 0);
        headerBar.Controls.Add(customerStack, 1, 0);
        headerBar.Controls.Add(logoutBox, 2, 0);

        // Row 1: Gold Divider Strip + Breathing Room Spacer
        Panel dividerAndBreathingSpace = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = _brightBg
        };
        Panel goldStripe = new Panel
        {
            Dock = DockStyle.Top,
            Height = 5,
            BackColor = _headerGold
        };
        dividerAndBreathingSpace.Controls.Add(goldStripe);

        // Row 2: Workspace Body Grid
        _bodyGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(20, 6, 20, 20),
            BackColor = _brightBg
        };
        _bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290f));
        _bodyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _bodyGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Left ATM Services Card
        _menuCard = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 16, 0),
            BackColor = _cardWhite,
            Padding = new Padding(14, 16, 14, 16)
        };

        _lblMenuHeader = new Label
        {
            Text = "ATM SERVICES",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 38,
            TextAlign = ContentAlignment.MiddleLeft
        };

        TableLayoutPanel menuButtonsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 365,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0)
        };
        for (int i = 0; i < 5; i++)
            menuButtonsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

        menuButtonsLayout.Controls.Add(CreateAtmMenuButton("1. Balance Inquiry", () =>
        {
            ActivateServicePanel(_panelBalanceInquiry);
        }), 0, 0);

        menuButtonsLayout.Controls.Add(CreateAtmMenuButton("2. Cash Deposit", () =>
        {
            ActivateServicePanel(_panelDeposit);
        }), 0, 1);

        menuButtonsLayout.Controls.Add(CreateAtmMenuButton("3. Cash Withdrawal", () =>
        {
            ActivateServicePanel(_panelWithdraw);
        }), 0, 2);

        menuButtonsLayout.Controls.Add(CreateAtmMenuButton("4. Funds Transfer", () =>
        {
            ActivateServicePanel(_panelTransfer);
        }), 0, 3);

        menuButtonsLayout.Controls.Add(CreateAtmMenuButton("5. Change PIN", () =>
        {
            ActivateServicePanel(_panelChangePin);
        }), 0, 4);

        _menuCard.Controls.Add(menuButtonsLayout);
        _menuCard.Controls.Add(_lblMenuHeader);

        // Right Service Stage
        _serviceStage = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _cardWhite,
            Padding = new Padding(24, 20, 24, 20),
            AutoScroll = true
        };

        BuildFiveServicePanels();

        _bodyGrid.Controls.Add(_menuCard, 0, 0);
        _bodyGrid.Controls.Add(_serviceStage, 1, 0);

        masterScreenGrid.Controls.Add(headerBar, 0, 0);
        masterScreenGrid.Controls.Add(dividerAndBreathingSpace, 0, 1);
        masterScreenGrid.Controls.Add(_bodyGrid, 0, 2);

        _atmServiceScreen.Controls.Add(masterScreenGrid);
    }

    private Button CreateAtmMenuButton(string title, Action onClick)
    {
        Button btn = new Button
        {
            Text = title,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 0, 5),
            BackColor = Color.FromArgb(238, 247, 242),
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 4, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(180, 215, 198);
        btn.FlatAppearance.BorderSize = 1;
        btn.Click += (s, e) => onClick();
        _menuButtons.Add(btn);
        return btn;
    }

    // =========================================================================
    // 3. THE 5 ATM SERVICE SCREENS (EQUAL BOX SIZES & RESPONSIVE FIT)
    // =========================================================================
    private void BuildFiveServicePanels()
    {
        // ---------------------------------------------------------------------
        // 1. BALANCE INQUIRY
        // ---------------------------------------------------------------------
        _panelBalanceInquiry = new Panel { Dock = DockStyle.Fill };

        TableLayoutPanel balLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3
        };
        balLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        balLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        balLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 215f));
        balLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75f));

        Label lblBalHeading = new Label
        {
            Text = "ACCOUNT BALANCE INQUIRY",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Panel summaryCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(236, 248, 241),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(20, 14, 20, 14)
        };

        TableLayoutPanel summaryInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        summaryInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        summaryInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        summaryInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        summaryInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _lblBalHolderName = new Label
        {
            Text = "CUSTOMER NAME: -",
            ForeColor = _textDark,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _lblBalAccountNo = new Label
        {
            Text = "ACCOUNT NO: -  |  BANK: IMARA BANK KENYA",
            ForeColor = _textMuted,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Label lblAvailCaption = new Label
        {
            Text = "AVAILABLE ACCOUNT BALANCE:",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };

        _lblBigBalance = new Label
        {
            Text = "KES 0.00",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        summaryInner.Controls.Add(_lblBalHolderName, 0, 0);
        summaryInner.Controls.Add(_lblBalAccountNo, 0, 1);
        summaryInner.Controls.Add(lblAvailCaption, 0, 2);
        summaryInner.Controls.Add(_lblBigBalance, 0, 3);
        summaryCard.Controls.Add(summaryInner);

        Panel btnReceiptHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 14, 0, 0) };
        Button btnDownloadReceipt = new Button
        {
            Text = "PRINT / DOWNLOAD OFFICIAL BALANCE RECEIPT",
            AutoSize = true,
            MinimumSize = new Size(340, 48),
            Padding = new Padding(18, 6, 18, 6),
            Location = new Point(0, 14),
            BackColor = _brandEmerald,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnDownloadReceipt.FlatAppearance.BorderSize = 0;
        btnDownloadReceipt.Click += (s, e) =>
        {
            if (_currentAccount == null) return;
            string savedPath = DatabaseManager.SaveBalanceInquiryReceipt(_currentAccount);
            MessageBox.Show(
                $"Official Balance Receipt generated with Bank Details, Customer Name, and Account Number.\n\nSaved to folder:\n{savedPath}",
                "Balance Receipt Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        };
        btnReceiptHolder.Controls.Add(btnDownloadReceipt);

        balLayout.Controls.Add(lblBalHeading, 0, 0);
        balLayout.Controls.Add(summaryCard, 0, 1);
        balLayout.Controls.Add(btnReceiptHolder, 0, 2);
        _panelBalanceInquiry.Controls.Add(balLayout);

        // ---------------------------------------------------------------------
        // 2. CASH DEPOSIT (5 IDENTICAL-SIZED KES BOXES)
        // ---------------------------------------------------------------------
        _panelDeposit = new Panel { Dock = DockStyle.Fill };

        TableLayoutPanel depLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5
        };
        depLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        depLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        depLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        depLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        depLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
        depLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));

        Label lblDepTitle = new Label
        {
            Text = "INSTANT ATM CASH DEPOSIT (KES)",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _lblDepSub = new Label
        {
            Text = "Select a KES value below or enter a custom deposit amount:",
            ForeColor = _textMuted,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Panel depInputBox = new Panel { Dock = DockStyle.Fill };
        _txtDepositAmount = new TextBox
        {
            Size = new Size(340, 40),
            Location = new Point(0, 6),
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            BackColor = Color.FromArgb(244, 249, 246),
            ForeColor = _brandEmerald,
            BorderStyle = BorderStyle.FixedSingle
        };
        depInputBox.Controls.Add(_txtDepositAmount);

        Control depQuickGrid = CreateEqualQuickAmountGrid(
            new[] { "KES 1,000", "KES 2,000", "KES 5,000", "KES 10,000", "KES 20,000" },
            new[] { "1000", "2000", "5000", "10000", "20000" },
            val => _txtDepositAmount.Text = val
        );

        Panel depBtnHolder = new Panel { Dock = DockStyle.Fill };
        Button btnSubmitDeposit = new Button
        {
            Text = "CONFIRM CASH DEPOSIT",
            Size = new Size(340, 48),
            Location = new Point(0, 8),
            BackColor = _brandBrightGreen,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSubmitDeposit.FlatAppearance.BorderSize = 0;
        btnSubmitDeposit.Click += BtnSubmitDeposit_Click;
        depBtnHolder.Controls.Add(btnSubmitDeposit);

        depLayout.Controls.Add(lblDepTitle, 0, 0);
        depLayout.Controls.Add(_lblDepSub, 0, 1);
        depLayout.Controls.Add(depInputBox, 0, 2);
        depLayout.Controls.Add(depQuickGrid, 0, 3);
        depLayout.Controls.Add(depBtnHolder, 0, 4);
        _panelDeposit.Controls.Add(depLayout);

        // ---------------------------------------------------------------------
        // 3. CASH WITHDRAWAL (MULTIPLES OF 100 | MIN: KES 200 | MAX: KES 40,000)
        // ---------------------------------------------------------------------
        _panelWithdraw = new Panel { Dock = DockStyle.Fill };

        TableLayoutPanel witLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5
        };
        witLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        witLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        witLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        witLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        witLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
        witLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));

        Label lblWitTitle = new Label
        {
            Text = "ATM CASH WITHDRAWAL (KES)",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Concise text & responsive font ensures "Max: KES 40,000" is 100% visible when minimized
        _lblWitSub = new Label
        {
            Text = "Multiples of KES 100 only (Min: KES 200 | Max: KES 40,000):",
            ForeColor = _textMuted,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Panel witInputBox = new Panel { Dock = DockStyle.Fill };
        _txtWithdrawAmount = new TextBox
        {
            Size = new Size(340, 40),
            Location = new Point(0, 6),
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            BackColor = Color.FromArgb(244, 249, 246),
            ForeColor = _textDark,
            BorderStyle = BorderStyle.FixedSingle
        };
        witInputBox.Controls.Add(_txtWithdrawAmount);

        Control witQuickGrid = CreateEqualQuickAmountGrid(
            new[] { "KES 200", "KES 1,000", "KES 5,000", "KES 10,000", "KES 40,000" },
            new[] { "200", "1000", "5000", "10000", "40000" },
            val => _txtWithdrawAmount.Text = val
        );

        Panel witBtnHolder = new Panel { Dock = DockStyle.Fill };
        Button btnSubmitWithdraw = new Button
        {
            Text = "DISPENSE CASH NOW",
            Size = new Size(340, 48),
            Location = new Point(0, 8),
            BackColor = _brandEmerald,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSubmitWithdraw.FlatAppearance.BorderSize = 0;
        btnSubmitWithdraw.Click += BtnSubmitWithdraw_Click;
        witBtnHolder.Controls.Add(btnSubmitWithdraw);

        witLayout.Controls.Add(lblWitTitle, 0, 0);
        witLayout.Controls.Add(_lblWitSub, 0, 1);
        witLayout.Controls.Add(witInputBox, 0, 2);
        witLayout.Controls.Add(witQuickGrid, 0, 3);
        witLayout.Controls.Add(witBtnHolder, 0, 4);
        _panelWithdraw.Controls.Add(witLayout);

        // ---------------------------------------------------------------------
        // 4. FUNDS TRANSFER
        // ---------------------------------------------------------------------
        _panelTransfer = new Panel { Dock = DockStyle.Fill };

        TableLayoutPanel trfLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 7
        };
        trfLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        trfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));

        Label lblTrfTitle = new Label
        {
            Text = "IMARA BANK INTER-ACCOUNT FUNDS TRANSFER",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Label lblRecAcc = new Label
        {
            Text = "1. Enter Recipient Imara Bank Account Number:",
            ForeColor = _textDark,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };

        Panel recAccHolder = new Panel { Dock = DockStyle.Fill };
        _txtTransferRecipientAcc = new TextBox
        {
            Size = new Size(380, 38),
            Location = new Point(0, 6),
            Font = new Font("Consolas", 14f, FontStyle.Bold),
            BackColor = Color.FromArgb(244, 249, 246),
            ForeColor = _textDark,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtTransferRecipientAcc.TextChanged += TxtTransferRecipientAcc_TextChanged;
        recAccHolder.Controls.Add(_txtTransferRecipientAcc);

        _lblVerifiedRecipientName = new Label
        {
            Text = "Recipient Status: Enter a valid 12-digit Imara Bank Account Number above.",
            ForeColor = _textMuted,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Label lblTrfAmount = new Label
        {
            Text = "2. Enter Amount to Transfer (KES):",
            ForeColor = _textDark,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };

        Panel trfAmtHolder = new Panel { Dock = DockStyle.Fill };
        _txtTransferAmount = new TextBox
        {
            Size = new Size(380, 38),
            Location = new Point(0, 6),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            BackColor = Color.FromArgb(244, 249, 246),
            ForeColor = _textDark,
            BorderStyle = BorderStyle.FixedSingle
        };
        trfAmtHolder.Controls.Add(_txtTransferAmount);

        Panel trfBtnHolder = new Panel { Dock = DockStyle.Fill };
        Button btnExecuteTransfer = new Button
        {
            Text = "COMPLETE FUNDS TRANSFER",
            Size = new Size(380, 48),
            Location = new Point(0, 8),
            BackColor = _brandBrightGreen,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnExecuteTransfer.FlatAppearance.BorderSize = 0;
        btnExecuteTransfer.Click += BtnExecuteTransfer_Click;
        trfBtnHolder.Controls.Add(btnExecuteTransfer);

        trfLayout.Controls.Add(lblTrfTitle, 0, 0);
        trfLayout.Controls.Add(lblRecAcc, 0, 1);
        trfLayout.Controls.Add(recAccHolder, 0, 2);
        trfLayout.Controls.Add(_lblVerifiedRecipientName, 0, 3);
        trfLayout.Controls.Add(lblTrfAmount, 0, 4);
        trfLayout.Controls.Add(trfAmtHolder, 0, 5);
        trfLayout.Controls.Add(trfBtnHolder, 0, 6);
        _panelTransfer.Controls.Add(trfLayout);

        // ---------------------------------------------------------------------
        // 5. CHANGE 4-DIGIT PIN
        // ---------------------------------------------------------------------
        _panelChangePin = new Panel { Dock = DockStyle.Fill };

        TableLayoutPanel pinLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 8
        };
        pinLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        pinLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));

        Label lblPinHeader = new Label
        {
            Text = "CHANGE YOUR 4-DIGIT ATM PIN",
            ForeColor = _brandEmerald,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Label lblOldPin = new Label { Text = "Current 4-Digit PIN:", ForeColor = _textDark, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
        Panel oldPinHolder = new Panel { Dock = DockStyle.Fill };
        _txtCurrentPin = new TextBox { Size = new Size(340, 38), Location = new Point(0, 6), Font = new Font("Consolas", 14f, FontStyle.Bold), MaxLength = 4, PasswordChar = '●', BackColor = Color.FromArgb(244, 249, 246), BorderStyle = BorderStyle.FixedSingle };
        oldPinHolder.Controls.Add(_txtCurrentPin);

        Label lblNewPin = new Label { Text = "New Desired 4-Digit PIN:", ForeColor = _textDark, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
        Panel newPinHolder = new Panel { Dock = DockStyle.Fill };
        _txtNewPin = new TextBox { Size = new Size(340, 38), Location = new Point(0, 6), Font = new Font("Consolas", 14f, FontStyle.Bold), MaxLength = 4, PasswordChar = '●', BackColor = Color.FromArgb(244, 249, 246), BorderStyle = BorderStyle.FixedSingle };
        newPinHolder.Controls.Add(_txtNewPin);

        Label lblConfPin = new Label { Text = "Confirm New 4-Digit PIN:", ForeColor = _textDark, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
        Panel confPinHolder = new Panel { Dock = DockStyle.Fill };
        _txtConfirmNewPin = new TextBox { Size = new Size(340, 38), Location = new Point(0, 6), Font = new Font("Consolas", 14f, FontStyle.Bold), MaxLength = 4, PasswordChar = '●', BackColor = Color.FromArgb(244, 249, 246), BorderStyle = BorderStyle.FixedSingle };
        confPinHolder.Controls.Add(_txtConfirmNewPin);

        Panel savePinBtnHolder = new Panel { Dock = DockStyle.Fill };
        Button btnUpdatePin = new Button
        {
            Text = "SAVE NEW 4-DIGIT PIN",
            Size = new Size(340, 48),
            Location = new Point(0, 8),
            BackColor = _brandEmerald,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnUpdatePin.FlatAppearance.BorderSize = 0;
        btnUpdatePin.Click += BtnUpdatePin_Click;
        savePinBtnHolder.Controls.Add(btnUpdatePin);

        pinLayout.Controls.Add(lblPinHeader, 0, 0);
        pinLayout.Controls.Add(lblOldPin, 0, 1);
        pinLayout.Controls.Add(oldPinHolder, 0, 2);
        pinLayout.Controls.Add(lblNewPin, 0, 3);
        pinLayout.Controls.Add(newPinHolder, 0, 4);
        pinLayout.Controls.Add(lblConfPin, 0, 5);
        pinLayout.Controls.Add(confPinHolder, 0, 6);
        pinLayout.Controls.Add(savePinBtnHolder, 0, 7);
        _panelChangePin.Controls.Add(pinLayout);

        _serviceStage.Controls.AddRange(new Control[]
        {
            _panelBalanceInquiry, _panelDeposit, _panelWithdraw, _panelTransfer, _panelChangePin
        });
    }

    private Control CreateEqualQuickAmountGrid(string[] labels, string[] values, Action<string> onSelect)
    {
        TableLayoutPanel grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            MaximumSize = new Size(660, 56),
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 4)
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        for (int i = 0; i < 5; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            string labelText = labels[i];
            string rawValue = values[i];

            Button btn = new Button
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(0),
                BackColor = Color.FromArgb(234, 246, 239),
                ForeColor = _brandEmerald,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = _brandEmerald;
            btn.Click += (s, e) => onSelect(rawValue);
            _quickAmountButtons.Add(btn);
            grid.Controls.Add(btn, i, 0);
        }

        return grid;
    }

    private void ApplyResponsiveScaling()
    {
        if (_bodyGrid == null || _bodyGrid.ColumnStyles.Count < 2) return;

        bool isCompact = ClientSize.Width < 1180;

        _bodyGrid.ColumnStyles[0].Width = isCompact ? 285f : 330f;
        _bodyGrid.Padding = isCompact ? new Padding(14, 4, 14, 14) : new Padding(26, 8, 26, 24);
        _serviceStage.Padding = isCompact ? new Padding(16, 14, 16, 14) : new Padding(30, 24, 30, 24);

        float menuFontSize = isCompact ? 10f : 11.5f;
        foreach (var btn in _menuButtons)
        {
            btn.Font = new Font("Segoe UI", menuFontSize, FontStyle.Bold);
        }

        float kesFontSize = isCompact ? 8.8f : 10f;
        foreach (var btn in _quickAmountButtons)
        {
            btn.Font = new Font("Segoe UI", kesFontSize, FontStyle.Bold);
        }

        // Top Header responsive fonts (ensures slogan and customer name never cut off)
        _lblHeaderBank.Font = new Font("Segoe UI", isCompact ? 12f : 15f, FontStyle.Bold);
        _lblHeaderSlogan.Font = new Font("Segoe UI", isCompact ? 8.2f : 10f, FontStyle.Italic);
        _lblTopCustomerName.Font = new Font("Segoe UI", isCompact ? 9.8f : 12.5f, FontStyle.Bold);
        _lblTopAccountNumber.Font = new Font("Consolas", isCompact ? 10f : 12f, FontStyle.Bold);
        _btnEjectCard.Font = new Font("Segoe UI", isCompact ? 8.8f : 10f, FontStyle.Bold);

        // Subtitles on Deposit & Withdrawal scale down in compact mode so "Max: KES 40,000" is never cut off
        if (_lblDepSub != null && _lblWitSub != null)
        {
            _lblDepSub.Font = new Font("Segoe UI", isCompact ? 9.2f : 10.5f, FontStyle.Regular);
            _lblWitSub.Font = new Font("Segoe UI", isCompact ? 9.2f : 10.5f, FontStyle.Bold);
        }

        if (_lblBalHolderName != null && _lblBalAccountNo != null && _lblBigBalance != null)
        {
            _lblBalHolderName.Font = new Font("Segoe UI", isCompact ? 10.5f : 12f, FontStyle.Bold);
            _lblBalAccountNo.Font = new Font("Segoe UI", isCompact ? 9.5f : 11f, FontStyle.Bold);
            _lblBigBalance.Font = new Font("Segoe UI", isCompact ? 22f : 26f, FontStyle.Bold);
        }
    }

    // =========================================================================
    // 4. ATM FLOW & REAL-TIME TRANSACTION LOGIC
    // =========================================================================
    private void ShowHeroPinScreen()
    {
        _inactivityTimer?.Stop();
        _currentAccount = null;
        _txtPinInput.Clear();
        _lblPinFeedback.Text = "Use the keypad or your keyboard to key in your 4-digit PIN.";
        _lblPinFeedback.ForeColor = _textMuted;

        _atmServiceScreen.Visible = false;
        _heroScreen.Visible = true;
        CenterLoginCardOnScreen();
    }

    private void ValidatePinAndLogin()
    {
        if (_failedPinAttempts >= 3)
        {
            DatabaseManager.LogSecurityEvent("CARD LOCKED - 3 FAILED ATTEMPTS");
            _lblPinFeedback.Text = "ATM CARD LOCKED: 3 incorrect PIN attempts. Restart terminal.";
            _lblPinFeedback.ForeColor = Color.Red;
            return;
        }

        string pin = _txtPinInput.Text.Trim();
        if (pin.Length != 4)
        {
            _lblPinFeedback.Text = "Please enter all 4 digits of your PIN.";
            _lblPinFeedback.ForeColor = Color.FromArgb(200, 60, 40);
            return;
        }

        var verifiedAccount = DatabaseManager.AuthenticateByPinOnly(pin);

        if (verifiedAccount != null)
        {
            _failedPinAttempts = 0;
            _currentAccount = verifiedAccount;
            _heroScreen.Visible = false;
            _atmServiceScreen.Visible = true;

            RefreshHeaderAndAccountState();
            ActivateServicePanel(_panelBalanceInquiry);
            ApplyResponsiveScaling();

            // Start the 60-second inactivity countdown timer
            ResetInactivityCountdown();
            _inactivityTimer.Start();
        }
        else
        {
            _failedPinAttempts++;
            DatabaseManager.LogSecurityEvent($"FAILED PIN ATTEMPT ({_failedPinAttempts} OF 3)");
            _txtPinInput.Clear();
            _lblPinFeedback.Text = $"INCORRECT 4-DIGIT PIN! Attempt {_failedPinAttempts} of 3.";
            _lblPinFeedback.ForeColor = Color.Red;
        }
    }

    private void ActivateServicePanel(Panel target)
    {
        foreach (Control c in _serviceStage.Controls)
            c.Visible = false;

        target.Visible = true;
        RefreshHeaderAndAccountState();
    }

    private void RefreshHeaderAndAccountState()
    {
        if (_currentAccount == null) return;
        var fresh = DatabaseManager.GetAccount(_currentAccount.AccountNumber);
        if (fresh == null) return;

        _currentAccount = fresh;
        _lblTopCustomerName.Text = $"ACCOUNT HOLDER: {_currentAccount.FullName}";
        _lblTopAccountNumber.Text = $"ACCOUNT NO: {_currentAccount.AccountNumber}";

        _lblBalHolderName.Text = $"CUSTOMER NAME: {_currentAccount.FullName}";
        _lblBalAccountNo.Text = $"ACCOUNT NO: {_currentAccount.AccountNumber}  |  BANK: IMARA BANK KENYA";
        _lblBigBalance.Text = $"KES {_currentAccount.Balance:N2}";
    }

    private void BtnSubmitDeposit_Click(object? sender, EventArgs e)
    {
        if (_currentAccount == null) return;

        if (!decimal.TryParse(_txtDepositAmount.Text.Trim(), out decimal amount))
        {
            MessageBox.Show("Please select or enter a valid numeric KES amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (DatabaseManager.Deposit(_currentAccount.AccountNumber, amount, out string msg))
        {
            _txtDepositAmount.Clear();
            RefreshHeaderAndAccountState();
            MessageBox.Show(msg, "Cash Deposit Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ActivateServicePanel(_panelBalanceInquiry);
        }
        else
        {
            MessageBox.Show(msg, "Deposit Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnSubmitWithdraw_Click(object? sender, EventArgs e)
    {
        if (_currentAccount == null) return;

        if (!decimal.TryParse(_txtWithdrawAmount.Text.Trim(), out decimal amount))
        {
            MessageBox.Show("Please select or enter a valid numeric KES amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (DatabaseManager.Withdraw(_currentAccount.AccountNumber, amount, out string msg))
        {
            _txtWithdrawAmount.Clear();
            RefreshHeaderAndAccountState();
            MessageBox.Show(msg, "Cash Dispensed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ActivateServicePanel(_panelBalanceInquiry);
        }
        else
        {
            MessageBox.Show(msg, "Withdrawal Declined", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TxtTransferRecipientAcc_TextChanged(object? sender, EventArgs e)
    {
        string accNo = _txtTransferRecipientAcc.Text.Trim();
        var target = DatabaseManager.GetAccount(accNo);
        if (target != null)
        {
            _lblVerifiedRecipientName.Text = $"✓ Verified Recipient: {target.FullName} (A/C: {target.AccountNumber})";
            _lblVerifiedRecipientName.ForeColor = _brandEmerald;
        }
        else
        {
            _lblVerifiedRecipientName.Text = "Recipient Status: Enter a valid 12-digit Imara Bank Account Number above.";
            _lblVerifiedRecipientName.ForeColor = Color.FromArgb(180, 60, 40);
        }
    }

    private void BtnExecuteTransfer_Click(object? sender, EventArgs e)
    {
        if (_currentAccount == null) return;

        if (!decimal.TryParse(_txtTransferAmount.Text.Trim(), out decimal amount))
        {
            MessageBox.Show("Please enter a valid numeric transfer amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string recipientAcc = _txtTransferRecipientAcc.Text.Trim();
        if (DatabaseManager.TransferFunds(_currentAccount.AccountNumber, recipientAcc, amount, out string msg))
        {
            _txtTransferRecipientAcc.Clear();
            _txtTransferAmount.Clear();
            RefreshHeaderAndAccountState();
            MessageBox.Show(msg, "Funds Transfer Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ActivateServicePanel(_panelBalanceInquiry);
        }
        else
        {
            MessageBox.Show(msg, "Transfer Declined", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnUpdatePin_Click(object? sender, EventArgs e)
    {
        if (_currentAccount == null) return;

        if (_txtNewPin.Text.Trim() != _txtConfirmNewPin.Text.Trim())
        {
            MessageBox.Show("Your new 4-digit PIN and confirmation PIN do not match.", "PIN Mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (DatabaseManager.ChangePin(_currentAccount.AccountNumber, _txtCurrentPin.Text.Trim(), _txtNewPin.Text.Trim(), out string msg))
        {
            _txtCurrentPin.Clear();
            _txtNewPin.Clear();
            _txtConfirmNewPin.Clear();
            RefreshHeaderAndAccountState();
            MessageBox.Show(msg, "PIN Changed Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ActivateServicePanel(_panelBalanceInquiry);
        }
        else
        {
            MessageBox.Show(msg, "PIN Change Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}