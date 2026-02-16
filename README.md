# ClipShelf

ClipShelf is a lightweight Windows desktop application designed to enhance clipboard management. It allows users to monitor clipboard changes, view clipboard history, and access clipboard items quickly through a user-friendly interface and system tray functionality.

## Features

- **Clipboard Monitoring**: Automatically tracks clipboard changes and saves new entries.
- **Clipboard History**: Displays a history of clipboard items in a user-friendly view.
- **System Tray Functionality**: Minimizes to the system tray, providing quick access to the application and options to clear history or exit.
- **Hotkey Registration**: Supports global hotkeys (Ctrl+Shift+V) to quickly open the main window.

## Project Structure

```
ClipShelf
├── src
│   ├── ClipShelf.csproj
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── Views
│   │   ├── HistoryView.xaml
│   │   └── HistoryView.xaml.cs
│   ├── ViewModels
│   │   ├── MainViewModel.cs
│   │   └── ClipboardViewModel.cs
│   ├── Models
│   │   └── ClipboardItem.cs
│   └── Services
│       ├── ClipboardService.cs
│       ├── HotkeyService.cs
│       └── TrayService.cs
├── ClipShelf.sln
├── .gitignore
└── README.md
```

## Getting Started

To build and run the ClipShelf application, follow these steps:

1. **Clone the Repository**: 
   ```
   git clone <repository-url>
   cd ClipShelf
   ```

2. **Open the Solution**: Open `ClipShelf.sln` in your preferred IDE.

3. **Restore Dependencies**: Ensure all dependencies are restored. This can typically be done through the IDE or by running:
   ```
   dotnet restore
   ```

4. **Build the Application**: Build the project to ensure everything is set up correctly:
   ```
   dotnet build
   ```

5. **Run the Application**: Start the application:
   ```
   dotnet run --project src/ClipShelf.csproj
   ```

## Contributing

Contributions are welcome! If you have suggestions for improvements or new features, feel free to open an issue or submit a pull request.

## License

This project is licensed under the MIT License. See the LICENSE file for more details.