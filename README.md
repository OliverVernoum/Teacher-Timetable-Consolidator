# Timetable Checker

A desktop app for Windows and Mac. It loads every teacher's timetable and shows **which teachers have a free period** at any time. It can also export timetables to **Apple Calendar, Outlook or Google Calendar**.

## Features

- **Who's free?** Pick a day and period, or click **Free right now**, to list the free teachers. The app also lists who is teaching and what.
- **Teacher timetable:** see any teacher's week as a grid, with free periods in green.
- **Add, rename, edit and remove teachers** inside the app. Changes are saved to the CSV files.
- **School day settings:** choose when the day starts, the number of periods, the length of each period, and when breaks and lunches happen. Breaks show in the timetable, and "Free right now" knows when it's break time.
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
- `Start` and `End` are filled in automatically from **School day settings**. If you write CSV files by hand without a `school-day.json`, the times in the files are used, and gaps between periods are shown as breaks.
- Day columns can be any days of the week (e.g. add `Saturday`). Short names like `Mon` also work.

The app opens the last folder you used. Otherwise it uses a `timetables` folder next to the app, or `Documents/Timetable Checker/timetables`. Use **Open timetables folder…** to choose another folder, e.g. a shared drive.

## School day settings

Click **School day settings…** at the top of the window to change the shape of the day for the whole school:

- **School day starts at:** the time the first period begins, e.g. `08:50`.
- **Number of periods:** from 1 to 15.
- **Period length:** set one length and click **Make every period this long**, or give individual periods a different length (e.g. a 70-minute P3). You can also rename periods, e.g. `Period 1` or `Reg`.
- **Breaks and lunches:** add as many as you like. Each one has a name, the period it comes after, and a length in minutes.

As you type, the preview on the right shows every period's and break's start and end time. Click **Save** to update every teacher's timetable. Lessons keep their position: whatever was in the 3rd period is still in the 3rd period, even if it's been renamed or moved to a new time. If you reduce the number of periods, the app warns you how many lessons will be deleted before it removes them.

The settings are saved as `school-day.json` in the timetables folder, so everyone using a shared folder sees the same school day.

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

Mac apps must be signed before they will run. `build-release.sh` signs them automatically when run on a Mac. The **Build apps** GitHub workflow builds everything on GitHub's own Macs.

### Making a release

- On GitHub, go to **Releases → Draft a new release**, type a new tag such as `v1.1`, and click **Publish release**. After about 5 minutes the Windows and Mac zips appear on the release.
- Or go to **Actions → Build apps → Run workflow** and type a version such as `v1.1`. This creates the tag and the release for you.

If you leave the version blank, the workflow just builds the zips, and you can download them from that run's page.

## Project layout

```
src/School.cs          loading/saving timetables, finding free teachers
src/CalendarExport.cs  .ics calendar file export
src/MainWindow.cs      the window
src/DaySchedule.cs     period lengths, breaks and lunches (school-day.json)
src/SchoolDayWindow.cs the School day settings window
src/Dialogs.cs         pop-up "type a name" / "are you sure?" boxes
timetables/            example timetables
```
