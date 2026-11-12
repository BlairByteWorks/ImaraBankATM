using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ImaraBankATM;

public class BankAccount
{
    public string AccountNumber { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public bool IsDefaultPin { get; set; }
}

public static class DatabaseManager
{
    private const string DbFile = "imara_atm.db";
    private const string ConnectionString = $"Data Source={DbFile}";

    public static void InitializeDatabase()
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Accounts (
                AccountNumber TEXT PRIMARY KEY,
                CardNumber TEXT NOT NULL UNIQUE,
                FullName TEXT NOT NULL,
                PinHash TEXT NOT NULL,
                Balance REAL NOT NULL,
                IsDefaultPin INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Transactions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ReferenceCode TEXT NOT NULL,
                AccountNumber TEXT NOT NULL,
                Type TEXT NOT NULL,
                Amount REAL NOT NULL,
                BalanceAfter REAL NOT NULL,
                Timestamp TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM Accounts;";
        long count = (long)(checkCmd.ExecuteScalar() ?? 0);

        if (count == 0)
        {
            SeedAccount("011094827100", "4539-8812-0041-9001", "WANJIKU KAMAU", "1234", 65400.00m);
            SeedAccount("011094827101", "4539-8812-0041-9002", "OTIENO OMONDI", "2580", 28500.00m);
            SeedAccount("011094827102", "4539-8812-0041-9003", "AMINA HASSAN", "4321", 142000.00m);
        }
    }

    public static string HashPin(string pin)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes("IMARA_ATM_PIN_" + pin));
        return Convert.ToHexString(bytes);
    }

    private static void SeedAccount(string accNo, string cardNo, string fullName, string rawPin, decimal openingBalance)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Accounts (AccountNumber, CardNumber, FullName, PinHash, Balance, IsDefaultPin)
            VALUES ($a, $c, $f, $p, $b, 1);
        ";
        cmd.Parameters.AddWithValue("$a", accNo);
        cmd.Parameters.AddWithValue("$c", cardNo);
        cmd.Parameters.AddWithValue("$f", fullName);
        cmd.Parameters.AddWithValue("$p", HashPin(rawPin));
        cmd.Parameters.AddWithValue("$b", (double)openingBalance);
        cmd.ExecuteNonQuery();

        LogTransaction(accNo, "OPENING BALANCE", openingBalance, openingBalance);
    }

    public static BankAccount? AuthenticateByPinOnly(string pin)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT AccountNumber, CardNumber, FullName, Balance, IsDefaultPin
            FROM Accounts WHERE PinHash = $p;
        ";
        cmd.Parameters.AddWithValue("$p", HashPin(pin.Trim()));

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var account = new BankAccount
            {
                AccountNumber = reader.GetString(0),
                CardNumber = reader.GetString(1),
                FullName = reader.GetString(2),
                Balance = Convert.ToDecimal(reader.GetDouble(3)),
                IsDefaultPin = reader.GetInt32(4) == 1
            };
            reader.Close();

            LogTransaction(account.AccountNumber, "SUCCESSFUL PIN LOGIN", 0m, account.Balance);
            return account;
        }
        return null;
    }

    public static BankAccount? GetAccount(string accountNumber)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT AccountNumber, CardNumber, FullName, Balance, IsDefaultPin
            FROM Accounts WHERE AccountNumber = $a;
        ";
        cmd.Parameters.AddWithValue("$a", accountNumber.Trim());

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new BankAccount
            {
                AccountNumber = reader.GetString(0),
                CardNumber = reader.GetString(1),
                FullName = reader.GetString(2),
                Balance = Convert.ToDecimal(reader.GetDouble(3)),
                IsDefaultPin = reader.GetInt32(4) == 1
            };
        }
        return null;
    }

    public static bool ChangePin(string accountNumber, string currentPin, string newPin, out string message)
    {
        var currentAcc = AuthenticateByPinOnly(currentPin);
        if (currentAcc == null || currentAcc.AccountNumber != accountNumber)
        {
            LogTransaction(accountNumber, "FAILED PIN CHANGE ATTEMPT", 0m, 0m);
            message = "The current 4-digit PIN you entered is incorrect.";
            return false;
        }

        if (newPin.Length != 4 || !int.TryParse(newPin, out _))
        {
            message = "Your new PIN must be exactly 4 numeric digits (e.g. 5678).";
            return false;
        }

        var existingPinOwner = AuthenticateByPinOnly(newPin);
        if (existingPinOwner != null && existingPinOwner.AccountNumber != accountNumber)
        {
            message = "Please choose a different 4-digit PIN.";
            return false;
        }

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET PinHash = $p, IsDefaultPin = 0 WHERE AccountNumber = $a;";
        cmd.Parameters.AddWithValue("$p", HashPin(newPin));
        cmd.Parameters.AddWithValue("$a", accountNumber);
        cmd.ExecuteNonQuery();

        LogTransaction(accountNumber, "PIN CHANGED SUCCESSFULLY", 0m, currentAcc.Balance);
        message = "Your 4-digit ATM PIN has been updated successfully.";
        return true;
    }

    public static bool Deposit(string accountNumber, decimal amount, out string message)
    {
        if (amount <= 0)
        {
            message = "Please enter a deposit amount greater than KES 0.";
            return false;
        }

        var acc = GetAccount(accountNumber);
        if (acc == null)
        {
            message = "Account not found.";
            return false;
        }

        decimal newBalance = acc.Balance + amount;
        UpdateBalance(accountNumber, newBalance);
        string refCode = LogTransaction(accountNumber, "CASH DEPOSIT", amount, newBalance);

        var updatedAcc = GetAccount(accountNumber)!;
        GenerateFormattedReceipt(updatedAcc, "CASH DEPOSIT", amount, refCode, "SELF CASH DEPOSIT", out string filePath);
        message = $"Successfully deposited KES {amount:N2}.\nNew Balance: KES {newBalance:N2}\n\nReceipt saved in folder:\n{filePath}";
        return true;
    }

    public static bool Withdraw(string accountNumber, decimal amount, out string message)
    {
        // 1. Enforce Kenyan ATM minimum withdrawal of KES 200
        if (amount < 200)
        {
            message = "Minimum ATM cash withdrawal amount is KES 200.00.";
            return false;
        }

        // 2. Enforce Kenyan ATM maximum withdrawal of KES 40,000
        if (amount > 40000)
        {
            message = "Maximum ATM cash withdrawal limit is KES 40,000.00 per transaction.";
            return false;
        }

        // 3. Enforce withdrawals strictly in multiples of KES 100
        if (amount % 100 != 0)
        {
            message = "Withdrawals must be in multiples of KES 100 (e.g., KES 200, 300, 500, 1,000).";
            return false;
        }

        var acc = GetAccount(accountNumber);
        if (acc == null)
        {
            message = "Account not found.";
            return false;
        }

        if (acc.Balance < amount)
        {
            LogTransaction(accountNumber, "DECLINED WITHDRAWAL (LOW FUNDS)", amount, acc.Balance);
            message = $"Insufficient funds. Your available balance is KES {acc.Balance:N2}.";
            return false;
        }

        decimal newBalance = acc.Balance - amount;
        UpdateBalance(accountNumber, newBalance);
        string refCode = LogTransaction(accountNumber, "CASH WITHDRAWAL", amount, newBalance);

        var updatedAcc = GetAccount(accountNumber)!;
        GenerateFormattedReceipt(updatedAcc, "ATM CASH WITHDRAWAL", amount, refCode, "CASH DISPENSED", out string filePath);
        message = $"Please take your cash: KES {amount:N2}.\nRemaining Balance: KES {newBalance:N2}\n\nReceipt saved in folder:\n{filePath}";
        return true;
    }

    public static bool TransferFunds(string senderAccountNo, string recipientAccountNo, decimal amount, out string message)
    {
        recipientAccountNo = recipientAccountNo.Trim();

        if (string.IsNullOrEmpty(recipientAccountNo))
        {
            message = "Please enter a recipient Account Number.";
            return false;
        }

        if (senderAccountNo == recipientAccountNo)
        {
            message = "You cannot transfer funds to your own account.";
            return false;
        }

        if (amount <= 0)
        {
            message = "Transfer amount must be greater than KES 0.";
            return false;
        }

        var sender = GetAccount(senderAccountNo);
        var recipient = GetAccount(recipientAccountNo);

        if (sender == null)
        {
            message = "Sender account error.";
            return false;
        }

        if (recipient == null)
        {
            message = $"Recipient Account Number '{recipientAccountNo}' was not found at Imara Bank.";
            return false;
        }

        if (sender.Balance < amount)
        {
            LogTransaction(sender.AccountNumber, "DECLINED TRANSFER (LOW FUNDS)", amount, sender.Balance);
            message = $"Insufficient funds. Available balance is KES {sender.Balance:N2}.";
            return false;
        }

        decimal senderNewBal = sender.Balance - amount;
        decimal recipientNewBal = recipient.Balance + amount;

        UpdateBalance(sender.AccountNumber, senderNewBal);
        UpdateBalance(recipient.AccountNumber, recipientNewBal);

        string refCode = LogTransaction(sender.AccountNumber, $"TRANSFER TO {recipient.AccountNumber}", amount, senderNewBal);
        LogTransaction(recipient.AccountNumber, $"TRANSFER FROM {sender.AccountNumber}", amount, recipientNewBal);

        var updatedSender = GetAccount(senderAccountNo)!;
        GenerateFormattedReceipt(
            updatedSender,
            "INTERNAL FUNDS TRANSFER",
            amount,
            refCode,
            $"TO: {recipient.FullName} (A/C: {recipient.AccountNumber})",
            out string filePath
        );

        message = $"Successfully transferred KES {amount:N2} to {recipient.FullName} (A/C: {recipient.AccountNumber}).\nRemaining Balance: KES {senderNewBal:N2}\n\nReceipt saved in folder:\n{filePath}";
        return true;
    }

    public static string SaveBalanceInquiryReceipt(BankAccount acc)
    {
        string refCode = "INQ" + DateTime.Now.ToString("MMddHHmmss") + new Random().Next(10, 99);
        LogTransaction(acc.AccountNumber, "BALANCE INQUIRY", 0m, acc.Balance);
        GenerateFormattedReceipt(acc, "BALANCE INQUIRY", 0m, refCode, "ACCOUNT STATUS: ACTIVE", out string savedFilePath);
        return savedFilePath;
    }

    public static void GenerateFormattedReceipt(BankAccount acc, string transType, decimal amount, string refCode, string extraDetails, out string savedFilePath)
    {
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Receipts");
        Directory.CreateDirectory(folder);

        string fileName = $"IBK_Receipt_{acc.AccountNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        savedFilePath = Path.Combine(folder, fileName);

        string maskedCard = acc.CardNumber.Length >= 4
            ? "XXXX-XXXX-XXXX-" + acc.CardNumber[^4..]
            : acc.CardNumber;

        var sb = new StringBuilder();
        sb.AppendLine("================================================");
        sb.AppendLine("             IMARA BANK KENYA LIMITED           ");
        sb.AppendLine("          Karibu Imara Bank. Pamoja Tunajenga.  ");
        sb.AppendLine("================================================");
        sb.AppendLine("TERMINAL ID    : IBK-ATM-NRB-001 (KENYATTA AVE) ");
        sb.AppendLine($"DATE & TIME    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"REFERENCE NO   : {refCode}");
        sb.AppendLine("------------------------------------------------");
        sb.AppendLine($"CUSTOMER NAME  : {acc.FullName}");
        sb.AppendLine($"ACCOUNT NUMBER : {acc.AccountNumber}");
        sb.AppendLine($"ATM CARD NO    : {maskedCard}");
        sb.AppendLine("------------------------------------------------");
        sb.AppendLine($"TRANSACTION    : {transType}");
        if (amount > 0)
        {
            sb.AppendLine($"TRAN AMOUNT    : KES {amount:N2}");
        }
        sb.AppendLine($"DETAILS        : {extraDetails}");
        sb.AppendLine("------------------------------------------------");
        sb.AppendLine($"LEDGER BALANCE : KES {acc.Balance:N2}");
        sb.AppendLine($"AVAIL BALANCE  : KES {acc.Balance:N2}");
        sb.AppendLine("================================================");
        sb.AppendLine("   THANK YOU FOR BANKING WITH IMARA BANK KENYA  ");
        sb.AppendLine("     Customer Care: 0800 721 000 | imarabank.ke ");
        sb.AppendLine("================================================");

        File.WriteAllText(savedFilePath, sb.ToString());
    }

    private static void UpdateBalance(string accountNumber, decimal newBalance)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET Balance = $b WHERE AccountNumber = $a;";
        cmd.Parameters.AddWithValue("$b", (double)newBalance);
        cmd.Parameters.AddWithValue("$a", accountNumber);
        cmd.ExecuteNonQuery();
    }

    private static string LogTransaction(string accountNumber, string type, decimal amount, decimal balanceAfter)
    {
        string refCode = "IBK" + DateTime.Now.ToString("MMddHHmmss") + new Random().Next(10, 99);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Transactions (ReferenceCode, AccountNumber, Type, Amount, BalanceAfter, Timestamp)
            VALUES ($r, $a, $t, $amt, $b, $ts);
        ";
        cmd.Parameters.AddWithValue("$r", refCode);
        cmd.Parameters.AddWithValue("$a", accountNumber);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$amt", (double)amount);
        cmd.Parameters.AddWithValue("$b", (double)balanceAfter);
        cmd.Parameters.AddWithValue("$ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        return refCode;
    }

    public static void LogSecurityEvent(string eventType, string accountOrTerminal = "TERMINAL-AUTH")
    {
        LogTransaction(accountOrTerminal, eventType, 0m, 0m);
    }
}