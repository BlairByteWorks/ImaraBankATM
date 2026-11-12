# Imara Bank Kenya (IBK) - 24/7 Smart ATM Terminal

A GUI-based Automated Teller Machine (ATM) simulation built with C# 12, .NET 8 Windows Forms, and SQLite. This project implements realistic Kenyan commercial banking rules, cryptographic PIN security, and dual-file data persistence.

Developed as a group laboratory project for **APT1030A: Fundamentals of Programming Languages** at the United States International University-Africa (USIU-Africa).

## 🚀 Features

* **Dual Data Persistence:** Uses a relational SQLite database (`imara_atm.db`) for accounts/transactions and the `System.IO` namespace to generate timestamped `.txt` receipt slips.
* **Cryptographic Security:** 4-digit customer PINs are salted and hashed using `SHA256.HashData()`. Plain text PINs are never stored.
* **Kenyan Banking Business Rules:** Enforces strict ATM withdrawal limits (Minimum KES 200, Maximum KES 40,000) and ensures cash is only dispensed in multiples of KES 100.
* **Forensic Audit Trail:** An append-only SQLite `Transactions` table permanently records successful logins, failed PIN attempts, lockouts, inactivity sign-outs, and all financial movements.
* **Real-Time Funds Transfer:** Validates recipient Imara Bank account numbers dynamically as the user types.
* **Session Security:** Implements a 3-strike PIN lockout and a 60-second inactivity automated sign-out timer using a global Windows message filter (`IMessageFilter`).
* **Responsive GUI:** Dynamically scales fonts, grid columns, and padding to prevent UI clipping in both minimized and full-screen modes.

## 🛠️ Technology Stack

* **Language:** C# 12
* **Framework:** Microsoft .NET 8.0 SDK (`net8.0-windows`)
* **GUI:** Windows Forms (WinForms)
* **Database:** SQLite (`Microsoft.Data.Sqlite`)

## ⚙️ Installation and Setup

1. **Prerequisites:** Ensure you have the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on your Windows machine.
2. **Clone the Repository:**
   ```powershell
   git clone [https://github.com/yourusername/ImaraBankATM.git](https://github.com/yourusername/ImaraBankATM.git)
   cd ImaraBankATM

**Build the Application:**
*dotnet build*

**Run the ATM:**
*dotnet run*

**Demo Accounts**
Upon the first launch, if the database is empty, the system automatically seeds three Kenyan demo accounts. You can use these to test the ATM:

Account Name	Account Number	4-Digit PIN	Opening Balance
Wanjiku Kamau	011094827100	1234	KES 65,400.00
Otieno Omondi	011094827101	2580	KES 28,500.00
Amina Hassan	011094827102	4321	KES 142,000.00

**Project Structure**
Program.cs: Application entry point and Windows Forms visual configuration.

MainForm.cs: The event-driven frontend GUI layer, dynamic scaling logic, and 60-second inactivity timer.

DatabaseManager.cs: The backend data access layer handling SQLite queries, transaction validation, SHA-256 hashing, and .txt receipt generation.

Assets/: Directory containing the custom brand background image.

Receipts/: Auto-generated folder where the application saves printable text receipts.