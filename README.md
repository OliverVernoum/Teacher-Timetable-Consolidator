# Timetable Checker

A desktop app for Windows and Mac. It loads every teacher's timetable and shows **which teachers have a free period** at any time. It can also export timetables to **Apple Calendar, Outlook or Google Calendar**.

## Features

- **Who's free?** Pick a day and period, or click **Free right now**, to list the free teachers. The app also lists who is teaching and what.
- **Teacher timetable:** see any teacher's week as a grid, with free periods in green.
- **Add, rename, edit and remove teachers** inside the app. Changes are saved to the CSV files.
- **Change period times** (e.g. P1 08:50–09:50) while editing. All teachers share the same times.
- **Calendar export (.ics):** export one teacher, or every teacher at once, as weekly repeating events. You choose the first week, how many weeks to repeat (e.g. a 13-week term), and whether to include free periods, lessons or both. Free periods are marked "available", so they don't block your calendar.

## Downloading and running

Download the zip for your computer from the repository's **Releases** page or the **Actions** tab (see *Building* below):

| Computer | Download | How to open |
|---|---|---|
| Windows | `TimetableChecker-Windows.zip` | Unzip, then double-click `TimetableChecker.exe`. If SmartScreen warns you, click **More info → Run anyway**. |
| Mac with an M1/M2/M3/M4 chip | `TimetableChecker-Mac-AppleSilicon.zip` | Unzip, then **right-click** `Timetable Checker.app` → **Open** → **Open** (only needed the first time). |
| Older Intel Mac | `TimetableChecker-Mac-Intel.zip` | Same as above. |

You don't need to install anything else. .NET is built into the app.

If macOS still says the app "is damaged", open Terminal in the unzipped folder and run:

```bash
xattr -cr "Timetable Checker.app"
```

## Timetable files

Each teacher is one CSV file in a folder, and **the file name is the teacher's name** (e.g. `Mr Smith.csv`). You can create these in the app with **Add teacher…**, or in Excel / Google Sheets and save as CSV:

```csv
Period,Start,End,Monday,Tuesday,Wednesday,Thursday,Friday
P1,08:50,09:50,10A Maths,,11B Maths,10A Maths,
P2,09:50,10:50,,9C Maths,9C Maths,,11B Maths
P3,11:10,12:10,11B Maths,10A Maths,,9C Maths,Free
```

- An **empty cell**, or the word **Free**, means a free period.
- `Start` and `End` are needed for **Free right now** and for calendar export.
- Day columns can be any days of the week (e.g. add `Saturday`). Short names like `Mon` also work.

The app opens the last folder you used. Otherwise it uses a `timetables` folder next to the app, or `Documents/Timetable Checker/timetables`. Use **Open timetables folder…** to choose another folder, e.g. a shared drive.

## Adding to Apple Calendar

1. Pick a teacher, set **First week** and **Repeat for (weeks)**, then click **Export to calendar (.ics)…**
2. Double-click the saved `.ics` file. Apple Calendar opens and asks which calendar to add the events to.
   - **iPhone/iPad:** AirDrop or email the file to yourself and tap it.
   - **Outlook:** File → Open & Export → Import/Export → iCalendar.
   - **Google Calendar:** Settings → Import & export → Import.

## Building it yourself

The app is written in C# with [Avalonia](https://avaloniaui.net/) (a cross-platform UI library) and needs the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src            # run it on this computer
./build-release.sh                  # build the Windows and Mac zips into ./publish
```

Mac apps must be signed before they will run. `build-release.sh` signs them automatically when run on a Mac. To build on GitHub's own Macs, use the **Build apps** workflow: open **Actions → Build apps → Run workflow** and download the zips when it finishes. Pushing a tag such as `v1.0` also attaches them to a Release.

## Project layout

```
src/School.cs          loading/saving timetables, finding free teachers
src/CalendarExport.cs  .ics calendar file export
src/MainWindow.cs      the window
src/Dialogs.cs         pop-up "type a name" / "are you sure?" boxes
timetables/            example timetables
```
