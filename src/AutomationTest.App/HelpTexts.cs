namespace AutomationTest.App
{
    /// <summary>The explanations behind the "?" icons.</summary>
    internal static class Help
    {
        private const string ExpressionBody = @"
VARIABLES
  {abc}               the value of the variable whose ID is abc
  {abc.Position.X}    a property of a variable (see PROPERTIES)
  {abc.Position.X + 1}  inside { } you can also calculate

VALUES
  12   3.5            numbers (always with a dot, on every PC)
  ""hello""  'hello'    text
  true  false         booleans
  now                 the current date and time
  today               today's date (time 00:00)
  {1,1}               a Position (x, y)
  {x,y,w,h}           a Range, for example {0,0,300,200}

OPERATORS (highest priority first)
  ( )                 grouping
  -  !                minus, not
  *  /  %             multiply, divide, remainder
  +  -                add, subtract. + also joins text:  ""Total: "" + {n}
  <  <=  >  >=        compare
  =  ==  !=           equal, not equal (= and == mean the same)
  &&                  and   (stops early when the left side is false)
  ||                  or    (stops early when the left side is true)

DATES
  {d} + 1             one day later (also works for date and time)
  {d2} - {d1}         the number of days between two dates
  {d} > today         dates and times can be compared
  A date can also be written as text, for example {d} = ""2026-11-06""

POSITIONS
  {a} + {b}           adds two Positions
  {a} - {b}           subtracts
  {a} * 2             scales a Position by a number

PROPERTIES (case does not matter)
  Position            X, Y
  Size                Width, Height
  Range               X, Y, Width, Height, Position, Size
  Image variable      Found, Position, Size
                      (the ID of an Image action; Position and Size can only
                      be read when Found is true)

SPECIAL WORDS
  true, false, now, today are reserved: they cannot be used as an ID.
  IDs are not case-sensitive: {ABC} and {abc} are the same variable.

ERRORS
  A variable that has no value yet stops the script and the message names it.
  A syntax error shows the position of the problem.
";

        private static HelpTopic Expression(string title, string intro)
        {
            return new HelpTopic(title, intro.Trim() + "\n" + ExpressionBody);
        }

        public static readonly HelpTopic ExpressionVariable = Expression("Expression of a Variable",
@"The result of the expression is stored in the variable, converted to the
chosen Value type. Using the same ID again changes the variable.

Examples
  {abc} + 1                       increases the Number abc by one
  {first} + "" "" + {last}          joins text
  {d} + 7                         a date one week later
  {p} + {10,0}                    moves a Position 10 pixels right");

        public static readonly HelpTopic ExpressionPosition = Expression("Position from a variable",
@"The result must be a Position, or two numbers separated by a comma.

Examples
  {img.Position}
  {img.Position} + {10,20}
  {img.Position.X + 1}, {img.Position.Y + 1}

A Position coming from an Image with a Process is an offset from that window's
Anchor. Use the same Process and Anchor in this Mouse action so the point lands
on the right place (the Image's own anchor does not matter here). Without a
Process the Position is a point on the desktop.");

        public static readonly HelpTopic ExpressionText = Expression("Text from a variable",
@"The result is converted to text and typed. Numbers drop a useless "".0"",
dates are written yyyy-MM-dd, a Position as x,y.

Examples
  {name}
  ""Total: "" + {sum}
  ""Order "" + ({n} + 1)");

        public static readonly HelpTopic ExpressionCase = Expression("Condition of a case",
@"The expression must give true or false. A Condition works like a
switch-case: the cases are checked from top to bottom and only the actions of
the FIRST case that is true run. When no case is true, the else runs.

Because the first true case wins, put the most specific case first.
When you create a Condition you type its first case here; add more cases with
Action = Case while the Condition (or something inside it) is selected.

Examples
  {abc} > 3
  {abc} > 2 && {abc} <= 3
  {img.Found}
  {name} = ""yes""
  {d} < today");

        public static readonly HelpTopic TitleRegex = new HelpTopic("Title (regular expression)",
@"Matches the window title. Case does not matter. Leave it empty to accept
any title. The window must also match the Process name if one is given.

Examples
  Notepad            the title contains Notepad
  ^Untitled          the title starts with Untitled
  report\.xlsx$      the title ends with report.xlsx
  (Chrome|Edge)      either word
  ^Calculator$       exactly this title
  .*                 anything

Special characters  . * + ? ( ) [ ] { } | ^ $ \  have a meaning. To match one
of them literally put a backslash before it, for example \. or \(.

Use the title to tell apart several windows of the same program.
Process is the program name without .exe (use the Pick button next to Process).");

        public static readonly HelpTopic Count = new HelpTopic("Times",
@"How many times the actions inside the Loop run (Type = Times).

  0        = forever: repeat until you stop the script (the Start key or Esc)
  1, 2...  repeat that many times");

        public static readonly HelpTopic Anchor = new HelpTopic("Anchor",
@"Used only when a Process is set. X and Y are measured from this point of the
program window's client area (the area without the title bar and borders):

  TopLeft      the top-left corner (default)
  TopRight     the top-right corner
  BottomLeft   the bottom-left corner
  BottomRight  the bottom-right corner
  Center       the middle of the window

Choose the anchor closest to the thing you click. Then the click stays correct
when the window is moved, and also when it is resized (for example a button at
the bottom-right keeps its distance to the bottom-right corner).

Without a Process, X and Y are desktop coordinates (the top-left of the main
screen is 0,0) and the anchor does not matter.

If you change the anchor after picking a point, pick the point again: the
numbers stay but their meaning changes.");

        public static readonly HelpTopic RangeAnchor = new HelpTopic("Anchor (Image)",
@"Used only when a Process is set. The Image action has ONE anchor, used for the
search range and for the position it reports:

  Range (px) X, Y    where the range's matching corner sits, measured from the
                     same corner of the program window's client area
  Position (result)  the found picture's matching corner measured the same way,
                     stored in the Image's variable: {id.Position}

Example: window 700x700, range 200x200, picture 100x100 in the middle of it.
  TopLeft      range Position 100,100  ->  {id.Position} = 150,150
  BottomRight  range Position -400,-400 -> {id.Position} = -450,-450
               (negative = inside the window, measured from the bottom-right)

A Mouse action with the same Process and Anchor and the expression {id.Position}
lands on that corner of the picture. The Mouse action's own Anchor is all that
matters there; it does not need to know the Image's anchor.");

        public static readonly HelpTopic RangeSize = new HelpTopic("Range",
@"The area that is searched for the picture, in pixels.

X, Y            the range's anchor corner (see Anchor), as an offset from the same
                anchor point of the Process window. Without a Process: the
                top-left of the range on the desktop.
Width, Height   the size of the range (the row under X, Y).

Width and Height both 0 mean: search everything - the whole client area of the
Process window, or the whole desktop when there is no Process. X and Y are
ignored then. Set both Width and Height, or leave both at 0.

A smaller range is faster and avoids matching a look-alike elsewhere.");

        public static readonly HelpTopic Id = new HelpTopic("ID",
@"The name other actions use to read this variable: {id}.

  letters, digits and _, not starting with a digit
  not case-sensitive ({ABC} is {abc})
  not one of the reserved words true, false, now, today
  shared by Variable and Image actions: the same ID is the same variable

An Image action with an ID stores Found, Position and Size in it.
Variables live for one run and start empty every time you press Start.");

        public static readonly HelpTopic Accuracy = new HelpTopic("Accuracy",
@"How similar the screen must be to the picture, in percent (1-100).

The picture is compared by its pattern (edges, text, icons), not by overall
brightness, so a slightly brighter or noisier copy still matches. A button
with a different colour or without its text does not.

90 is a good start. Use Check to see the real percentage of what is on the
screen, then set a value a little below it. Too low finds look-alikes; too high
misses small changes. It does not help when the picture is partly covered.");

        public static readonly HelpTopic WaitSettle = new HelpTopic("Wait",
@"How long to wait after a matching window is found, before it is accepted.
The window must still be the match after this time. A program may show a splash
screen whose title matches too (Excel shows ""Opening - Excel"" for a moment before
its real window); a longer Wait makes sure the real window is the one used.
Default 500 ms. Scripts saved before this setting existed use 500 ms too.");

        public static readonly HelpTopic WaitTime = new HelpTopic("Timeout",
@"How long one attempt waits for the window to appear and come to the front.
If it does not, the attempt is repeated (see Retry). When all attempts fail the
script stops with an error. When the window is ready the actions inside run.");

        public static readonly HelpTopic Retry = new HelpTopic("Retry",
@"How many more attempts are made after the first one times out.
0 means a single attempt. Total waiting time is (Retry + 1) x Timeout.");

        public static readonly HelpTopic MouseTime = new HelpTopic("Time (Mouse)",
@"How long the cursor takes to glide from where it is to the position of this
action. A bigger value moves more slowly; 0 jumps at once.

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic MouseDelay = new HelpTopic("Delay (Mouse)",
@"How long to wait after this action has finished (after the click, scroll or
move), before the next action starts. Use it to give the program time to react.

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic KeyTime = new HelpTopic("Time (Keyboard)",
@"Key press / Combo key: how long the key(s) are held down.
Text: the total time spent typing, spread over all characters
(0 types as fast as possible).
Key down / Key up: not used.

Keys are sent to the window that has the focus: put a Click or a Wait before
the keyboard action when a specific program must receive them.

Can be given in milliseconds, seconds, minutes or hours.");

        // ---- Email
        public static readonly HelpTopic EmailId = new HelpTopic("ID (Email)",
@"Read: the variable that receives the messages, a table with one
row per message (newest first) and these columns:
  1 From   2 Subject   3 Date   4 Unread (true/false)
  5 Body (text)   6 Attachments (names, separated by |)
For example {mail[1, 2]} is the subject of the first message and
{mail.Rows} how many were found.

Mark and Delete: the variable receives how many messages were
changed (a Number). Leave it empty if you do not need it.");

        public static readonly HelpTopic EmailKind = new HelpTopic("Type (Email)",
@"Send: sends a message.
Read: reads the messages that match the filters into a table.
Mark: marks the messages that match the filters as read or unread.
Delete: deletes the messages that match the filters (into the
Trash folder, or for good). At least one filter is needed.

No Outlook or mail program is used: the account is an SMTP / IMAP
profile. Click the mail icon next to Category to add, edit and test
profiles.");

        public static readonly HelpTopic EmailProfile = new HelpTopic("Profile",
@"The mail account this action uses. Profiles are made with the
mail icon next to Category (server, port, security, user and
password, and a Test button).

A script only holds the NAME of the profile, never the password, so
the same script runs on another computer that has a profile with
that name.");

        public static readonly HelpTopic EmailTo = new HelpTopic("To, Cc, Bcc",
@"Addresses separated by commas or semicolons:
  ann@example.com, Bob <bob@example.com>
With Expr. on, each box is an expression that gives the text, for
example {boss} or ""a@x.com; "" + {other}.");

        public static readonly HelpTopic EmailSubject = new HelpTopic("Subject",
@"Send: the subject of the message.
Read, Mark, Delete: only messages whose subject CONTAINS this text
(letter case does not matter). Empty = any subject.");

        public static readonly HelpTopic EmailBody = new HelpTopic("Message",
@"The text of the message. With HTML on, the text is sent as HTML
(for example <b>bold</b>), otherwise as plain text.");

        public static readonly HelpTopic EmailAttach = new HelpTopic("Attach",
@"The files to attach, one path per line. A relative path starts at
the folder of the script. A missing file is an error.");

        public static readonly HelpTopic EmailFolder = new HelpTopic("Folder",
@"The mail folder to look in. Empty = INBOX. Other folders use the
name the server gives them, for example ""Sent"" or ""Work/Reports"".");

        public static readonly HelpTopic EmailFilter = new HelpTopic("Filters",
@"Only the messages that match ALL the filters are used:
  Subject has   the subject contains the text
  From          the sender address or name contains the text
  Status        Any, Unread or Read
  Since/Before  the day received (see the date help)
Empty filters match everything.");

        public static readonly HelpTopic EmailDates = new HelpTopic("Since, Before",
@"Days written as yyyy-MM-dd, for example 2026-11-30.
Since: received on that day or later. Before: received before that
day. Mail servers compare whole days, not hours.
With Expr. on, they are expressions, for example today - 7
(a Date value is turned into its day).");

        public static readonly HelpTopic EmailMax = new HelpTopic("Max",
@"At most this many messages are used, the newest first
(Read: how many are read; Mark and Delete: how many are changed).");

        public static readonly HelpTopic EmailMarkRead = new HelpTopic("Mark as read",
@"Read: mark the messages that were read as read on the server.
Off leaves unread messages unread.");

        public static readonly HelpTopic EmailSaveTo = new HelpTopic("Save files to",
@"Read: save the attachments of the messages into this folder (made
if missing; a name that exists gets (2), (3)...). The Attachments
column then holds the saved paths. Empty = do not save, the column
only holds the names.");

        public static readonly HelpTopic EmailDelete = new HelpTopic("For good",
@"Off: the messages are moved to the Trash folder of the account
(when the account has no Trash folder nothing is deleted and you get
an error: turn this on to delete anyway).
On: the messages are deleted for good and cannot be brought back.");

        public static readonly HelpTopic EmailTimeout = new HelpTopic("Timeout",
@"How long to wait for the mail server before giving up
(connecting, logging in, each answer).");

        public static readonly HelpTopic CatchId = new HelpTopic("Error ID (Catch)",
@"Try runs the actions inside it. When one of them fails, the
rest of the Try is skipped and the actions inside the Catch line
at its end run, and the run goes on after the Try.

If you give an ID here, the message of the error is stored in that
variable (a String), so you can use it: {error}.

Stopping the script with the Stop key is not an error, and the
Catch does not run. A disabled Catch means errors are not caught.
An error inside the Catch itself stops the run (or is caught by
a Try around this one).

Break, Continue and Return inside a Try work as everywhere else:
they leave the Try and do not count as an error.");

        public static readonly HelpTopic CommandId = new HelpTopic("ID (File/Folder)",
@"The variable that receives the result:

Exists   true or false (a Boolean)
Read     the text of the file (a String)
List     a table of one column with the full paths of the files and
         folders inside the folder: {id.Rows}, {id[1, 1]}...
         Use it with Loop (For) to go through them.");

        public static readonly HelpTopic CloseMatch = new HelpTopic("Match (Program Close and Window actions)",
@"How the programs to close are found:

Title    a regular expression, not case sensitive, on the title of
         the main window. Programs without a window never match.
Name     the process name, for example notepad (.exe is optional)
Path     the full path of the program file
Id       the process id (a number)

The Automation Test program itself is never closed. If no program
matches, Close does nothing (a note is shown, it is not an error);
the Window actions fail with an error instead.");

        public static readonly HelpTopic Api = new HelpTopic("API",
@"Calls a web address (http or https; the certificate of a secure
address is always checked) and keeps the answer in the variable of
the ID.

URL       the address. Expr. makes it an expression, for example
          ""https://site/items/"" + {id}.
Headers   one per line: Name: value. A value that starts with = is
          an expression:  Authorization: = ""Bearer "" + {token}
Body      Post, Put, Patch, Delete: the text to send. Text that starts
          with { or [ is sent as JSON, other text as plain text; a
          Content-Type header overrides that.
Save to   Download: the file to save the answer in (Overwrite to
          replace one that exists).
Timeout   how long to wait for the answer.
Fail on error
          ON: a status that is not 2xx (404, 500...) is an error
          (a Try can catch it). OFF: it is an answer you can test.

The variable holds the whole answer:
  {r.Status}       the number, 200, 404...
  {r.Ok}           true for 200 to 299
  {r.Reason}       the words, OK, Not Found
  {r.Body}         the answer as text
  {r.Json}         the answer read as JSON:
                     {r.Json.data[0].name}   an item, then a name
                     {r.Json[""first name""]}  a name with a space
                     {r.Json.items.Count}    how many items
                   (arrays count from 0; a JSON text, number or
                   true/false is an ordinary String, Number or Boolean)
  {r.Headers}      a table of the header names and values
  {r.ContentType}

Messages and the log name the address without its query (a query
often holds a key). Keep keys and passwords in a Password variable
and use it in an expression. Careful with scripts from other
people: an API action sends data to the address it names.");

        public static readonly HelpTopic AutoScript = new HelpTopic("Auto Script",
@"Runs another script file (saved from this program) as a part of
this one, so a job you made once can be used again.

File      the script file. A path without a drive starts in the
          folder of the script that calls it. Expr. makes it an
          expression.
Inputs    the values to hand over, one per line: name = expression.
          The expression is worked out here, with this script's
          variables; inside the other script {name} has that value.
Returns   the names of variables of the other script whose values
          come back here, separated by commas or lines. If it does
          not set one of them, the action fails.

The other script has variables of its own: it sees only its
inputs, and nothing else of it is visible here except the returns.
A Return inside it ends that script and goes back here. An error
inside it is an error of this action (a Try around it catches it).
It shares the Excel workbook, the browser tab and the passwords
of the run. A script that calls itself, or scripts nested more
than 16 deep, fail with an error.

Careful with scripts from other people: a script can delete your
files, close programs or shut the computer down.");

        public static readonly HelpTopic OcrLanguage = new HelpTopic("Language (OCR)",
@"OCR reads the text in an area of the screen with the text
recognition that comes with Windows 10 and 11, and stores it in the
variable as a String. Set the area like for Image: the Process
window, the Anchor, and a Range (Width and Height at 0 = the whole
window, or the whole screen without a Process).

Language: a tag such as vi, en-US or ja. Empty = the languages of
your Windows profile. Windows can only read the languages that have
OCR installed (Settings, Time and language, Language and region,
add the language and its text recognition). If the language is not
installed the action fails and names the ones that are.

Text on a plain background, large enough to read (about 10 pixels
or more for a letter), works best. Run the action alone with Start
Selected to see what is read: the variable can be used in a
Condition or typed with Keyboard Text.");

        public static readonly HelpTopic WindowPlace = new HelpTopic("Window Move and Resize",
@"The window is found like the programs of Close: by its Title
(a regular expression), the process Name, the Path of the program
or the process Id (see Match). Only visible windows with a title
count. Activate, Minimize, Maximize and Restore need nothing else.

Move  X and Y: where the top left corner of the window goes, in
      pixels on the screen (the top left of the main screen is 0, 0;
      a second screen to the left has negative X).
Resize  Width and Height of the whole window in pixels.

A minimized or maximized window is restored first, so it can be
moved or resized. If no window is found the action fails with an
error (a Try can catch it). A window of a program that runs as
administrator can only be changed when this program runs as
administrator too.");

        public static readonly HelpTopic CloseAll = new HelpTopic("All matches (Program Close)",
@"OFF   only the first program that matches is closed (the one with
      the lowest process id)
ON    every program that matches is closed

Be careful with a wide pattern: a Title such as .* matches every
program that has a window.");

        public static readonly HelpTopic CloseTimeout = new HelpTopic("Timeout (Program Close)",
@"The program is asked to close its window, as if you clicked the X.
This is how long to wait for it to end. A program that asks whether
to save first stays open: the action then fails with a message
(turn on Kill, or give it more time).

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic CloseKill = new HelpTopic("Kill (Program Close)",
@"OFF   the program is asked to close and can save its work (default)
ON    the program is ended at once; unsaved work is lost

A program that runs as administrator can only be closed when this
program runs as administrator too.");

        public static readonly HelpTopic FileTarget = new HelpTopic("Destination (File/Folder)",
@"Copy, Move  where the file or folder goes. An existing folder (or a
             path ending with \) means ""inside it, same name"";
             anything else is the full new path.
Rename       the new name only, without a folder.
Write        the text to write into the file.

Environment variables such as %TEMP% work in paths, and a path
without a drive is taken from the program's folder.");

        public static readonly HelpTopic FileOverwrite = new HelpTopic("Overwrite (File/Folder)",
@"OFF   an existing file or folder at the destination is an error
ON    a file is replaced; a folder is merged into (files with the
      same name are replaced, the rest stays)");

        public static readonly HelpTopic FileRecurse = new HelpTopic("Recurse (File/Folder)",
@"Delete  OFF: a folder that is not empty is an error.
         ON: the folder is deleted with everything inside it.
List    ON: also lists what is inside the subfolders.

Careful with scripts from other people: Delete removes your files
and cannot be undone (nothing goes to the Recycle Bin). A whole
drive is never deleted.");

        public static readonly HelpTopic FileAppend = new HelpTopic("Append (File/Folder)",
@"OFF   the file is replaced by the text
ON    the text is added at the end of the file

The file (and the folders above it) are made when they are missing.");

        public static readonly HelpTopic FileForce = new HelpTopic("Force (File/Folder Delete)",
@"ON   read-only files are deleted too (their read-only mark is
     removed first)

A path where nothing exists is not an error: a note is shown.");

        public static readonly HelpTopic ShutdownTime = new HelpTopic("Time (Shutdown)",
@"How long to wait before the computer is shut down. The Stop key
cancels the countdown.

Be careful with scripts from other people: a Shutdown action turns
your computer off.

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic ShutdownForce = new HelpTopic("Force Shutdown",
@"OFF   programs with unsaved work can stop the shutdown (default)
ON    other programs are closed even if they have unsaved work;
      that work is lost");

        public static readonly HelpTopic WebSelector = new HelpTopic("Selector (Web)",
@"Which element to use on the page.

A CSS selector:   #email    .btn-primary    input[name=q]    form button
text=Label        the element whose visible text contains Label
                  (not case sensitive), for example text=Sign in

Pick   click the Pick button, then move the mouse over the element in
       the browser and click it. The selector (and the Frame, when the
       element is inside an iframe) is filled in for you.

The action waits for the element to appear (see Timeout) and
scrolls it into view.");

        public static readonly HelpTopic WebUntil = new HelpTopic("Wait for",
@"Element appears     the element is on the page and visible (default)
Element disappears  the element is gone or hidden
Address matches     the address of the tab matches a regular
                    expression (not case sensitive), for example
                    /dashboard   or   ^https://shop\.
Title matches       the title of the tab matches
Page text           the text of the page (not of frames) matches

Each waits until the Timeout, then the action fails.");

        public static readonly HelpTopic WebClickType = new HelpTopic("Click type",
@"Click         one click with the left button
DoubleClick   two clicks in a row
RightClick    one click with the right button (opens the context
              menu of the page, if it has one)");

        public static readonly HelpTopic WebDrag = new HelpTopic("Drag (Drop on)",
@"Drags the element of Selector and drops it on the element of Drop on
(same Frame). The pointer moves in steps, so lists that follow the
mouse work; pages that use the browser's own drag and drop
(draggable=true) work too.

Hover and Drag need the tab in front: the browser window comes to
the front for the time of the action.");

        public static readonly HelpTopic WebScreenshot = new HelpTopic("Screenshot",
@"Saves a picture of the page in the file (.png or .jpg).

Selector given   just that element
Full page ON     the whole page, also what has to be scrolled to
Otherwise        what is visible in the tab now

The folder of the file must exist; an existing file is replaced.
Text is the file; with Expr. ON an expression, so the name can hold
a date or a counter.");

        public static readonly HelpTopic WebFrame = new HelpTopic("Frame",
@"Leave empty when the element is on the page itself.

When it is inside an iframe, enter the CSS selector of that iframe,
for example  iframe#editor . For frames inside frames, separate the
selectors with >>> , outermost first:  iframe.outer >>> iframe.inner

Frames of the same site and of other sites (an embedded payment
form or video page, for example) both work, also nested. The Pick
button does not look inside frames of other sites: type their
selector by hand.

An element inside a shadow root (a web component) is reached in
the Selector, not here: write  host-element >>> inner-element  , for
example  my-card >>> button.buy . Pick writes it for you. Only open
shadow roots can be entered.");

        public static readonly HelpTopic WebId = new HelpTopic("ID (Element Get)",
@"The variable that receives the text of the element: the value of an
input field, otherwise the visible text. Use it later as {id} in
expressions (Variable, Condition, Input, Keyboard text...).

Letters, digits and _, not starting with a digit.");

        public static readonly HelpTopic WebRequired = new HelpTopic("Required (Element Get)",
@"ON    the element must be on the page within the Timeout, otherwise
      the script stops with an error (default)
OFF   a missing element is not an error: the variable becomes empty
      text, so a Condition such as {price} == """" can choose what to
      do. Use a short Timeout for this.");

        public static readonly HelpTopic WebBrowser = new HelpTopic("Browser",
@"Which browser the action works in: Chrome or Edge. Both can be
open and used by the same script.

The actions inside a Tab New or a Tab Switch always use the browser
of that action: it is filled in for them and cannot be changed. To
work in the other browser, use a Tab New or Tab Switch action for it.

Tab Close closes the tab the script is using (the next action that
needs a tab uses the one in front: add a Tab New or Tab Switch).
Browser Close closes ALL windows of the browser, also the ones you
opened yourself; unsaved forms in them are lost.

Each browser needs the Automation Test extension (Load unpacked:
the AutomationWebHelper folder) and only one profile of a browser can
be connected at a time.");

        public static readonly HelpTopic ExcelId = new HelpTopic("ID (Excel)",
@"The variable that receives the result. Use it later as {id} in
expressions (Variable, Condition, Cell Set, Loop...).

Cell Get, Excel Info, Evaluate  the value, as a number, date, true/false
                              or text
Range Get                     a table: {id.Rows}, {id.Columns},
                              {id[row, column]} (see the ? of Value)
Find                          Found, Row, Column and Address:
                              {id.Found}, {id.Row}, {id.Column}, {id.Address}

Letters, digits and _, not starting with a digit.");

        public static readonly HelpTopic ExcelWorkbook = new HelpTopic("Workbook",
@"Makes a workbook the one the actions use. The actions inside this
action work in that workbook.

Path    the file. If it is open in Excel already (in any Excel
        window) the action switches to it; if not, it is opened.
        Excel is started first when it is not running. Empty:
        a new workbook is made.
        Excel cannot have two workbooks with the same file name
        open, even from different folders: that is reported.

Without a Workbook action the workbook that is active in Excel is
used. Macros of a file opened here do not run (see Allow macros).

A script's changes cannot be undone with Ctrl+Z: Excel forgets
its Undo history when a program changes the workbook.");

        public static readonly HelpTopic ExcelWorkbookName = new HelpTopic("Name (Workbook)",
@"Used when Path is empty: a regular expression for the name of a
workbook that is open already, not case sensitive. It reaches
workbooks that have no file yet (Book1) and workbooks you opened
yourself:

^Book1$      the new workbook Book1
sales        any workbook with sales in its name");

        public static readonly HelpTopic ExcelReadOnly = new HelpTopic("Read only",
@"Opens the file read-only, so it cannot be changed or locked
for others. Has no effect when the file is open already.");

        public static readonly HelpTopic ExcelVisible = new HelpTopic("Visible",
@"Only matters when this action has to start Excel.

ON    Excel is shown and stays open when the script ends
OFF   Excel runs hidden and is quit when the script ends

An Excel that was running before is never hidden or quit unless
you use Excel Close.");

        public static readonly HelpTopic ExcelAllowMacros = new HelpTopic("Allow macros",
@"OFF (default)  the macros of the file never run, whatever the file
                or the script says.
ON              the Trust Center of your Excel decides: macros run
                only if it allows them for this file (a trusted
                location, or a setting that enables all macros).
                Use it only for files you trust, and for an Excel Macro.");

        public static readonly HelpTopic ExcelInsert = new HelpTopic("Insert / Delete rows or columns",
@"Target   rows or columns
Count    how many
Cell     any cell in the first row (or column) to insert at or to
         delete; B5 with Rows and Count 2 is rows 5 and 6.

Insert pushes the existing rows down (columns to the right) and
leaves the new ones empty. Delete removes them and pulls up the
rest; what was in them is lost.");

        public static readonly HelpTopic ExcelClear = new HelpTopic("Clear",
@"Contents   values and formulas go; the formatting stays
Formats    the formatting goes; the values stay
All        both

Cell is the area, for example A2:D100, or a whole column B:B.");

        public static readonly HelpTopic ExcelCopy = new HelpTopic("Range Copy / Cut and Sheet Copy / Move",
@"Range Copy: copies the area (Cell) to the destination (Value, its
top-left cell). Whole rows and columns work too (2:5, B:D).
Range Cut: moves the area, with everything in it.

To workbook / To sheet (regular expressions on the names, not case
sensitive) say where it goes; empty means the same workbook / the
same sheet as the area (a workbook given without a sheet: its
active sheet).

Paste  All           values, formulas and formatting
       Values        just the values (formulas become their results)
       Formulas      the formulas (relative references shift like
                     in a paste), keeping the formatting there
       Formats       just the formatting
       Column widths just the column widths
Transpose swaps rows and columns.

Between workbooks that are open in separate Excel windows only
Values and Formulas (without Transpose) work; Range Cut and the
other kinds need both workbooks in the same Excel window. All the
kinds except Values and Formulas use the clipboard.

Sheet Copy / Sheet Move: the sheet is chosen by Sheet / Index (empty
= the current sheet). To workbook says where it goes (empty = the
same workbook, which is open already); Position says where in that
workbook (before or after its current sheet, at the start or the
end). Sheet Copy can name the copy (Value). Select it makes the
copy or the moved sheet the current one.");

        public static readonly HelpTopic ExcelReplace = new HelpTopic("Replace",
@"Replaces the text that Value finds with this text, in the cells of
the area (empty area = the whole sheet). Match case and Whole cell
work as in Find. Leave this empty to remove the text.");

        public static readonly HelpTopic ExcelVisibility = new HelpTopic("Visibility (Sheet Visible)",
@"Visible      the sheet is shown
Hidden       the sheet is hidden (the user can unhide it)
Very hidden  the sheet is hidden and Excel's Unhide menu does not
             list it

A workbook needs at least one visible sheet. A hidden sheet cannot
be the current one.");

        public static readonly HelpTopic ExcelFormat = new HelpTopic("Format",
@"Changes only the fields that are filled in; empty fields and Leave
keep what the cells have.

Num. format   0.00   #,##0   dd/mm/yyyy   hh:mm   0%   @ (text)
              (the names of the formats are those of English Excel)
Bold, Italic  Yes, No, or Leave
Colours       #RRGGBB, for example #FF0000 red, #FFFF00 yellow
Col. width    a number (characters), or auto to fit the content");

        public static readonly HelpTopic ExcelSort = new HelpTopic("Sort",
@"Sorts the rows of the area by one column.

Cell        the area, for example A1:D100
Sort by     the letters of the column, for example C (it has to be
            inside the area)
Descending  large to small, Z to A
Has header  the first row of the area is a header and stays on top");

        public static readonly HelpTopic ExcelFilter = new HelpTopic("Filter",
@"Cell         the area with its header row, for example A1:D100
Filter col.  which column of the area, 1 for its first column
Criterion    >5   <=10   =Hanoi   <>   (not empty)   *text*   =

The rows that do not match are hidden, not deleted. Another Filter
on a different column narrows it further. Clear filter removes the
filter of the sheet and shows every row again.");

        public static readonly HelpTopic ExcelMacro = new HelpTopic("Excel Macro",
@"Runs a macro (a Sub or Function) of a workbook that is open, for
example Module1.Report or 'sales.xlsm'!Report. One argument can be
given in Value. What a Function returns goes into the variable ID.

The Timeout does not stop a macro that runs long or waits for a
dialog; Stop works after it has finished. Use a visible Excel.

WARNING: a macro can do anything on this computer. Run only macros
you trust, and do not run a script from someone else that uses
this action without reading it. The workbook has to allow macros
(Workbook, Allow macros), and your Trust Center has to agree.");

        public static readonly HelpTopic ExcelUseTarget = new HelpTopic("In workbook / In sheet",
@"Optional. Make this action work in another open workbook and/or
sheet, for this action only: the workbook and sheet that the
following actions use do not change.

Both are regular expressions on the names, not case sensitive.
Empty = the workbook / sheet in use (set by Workbook and Select
Sheet, otherwise the active ones). A workbook given without a sheet
uses that workbook's active sheet.

The workbook has to be open in Excel already; use Workbook to open it.");

        public static readonly HelpTopic ExcelSheet = new HelpTopic("Sheet",
@"Sheet Select, Sheet Delete: a regular expression for the name of
the sheet, not case sensitive (empty: only the Index counts).
Sheet Create: the name of the new sheet (empty: Excel names it).

Sheet names have at most 31 characters and none of : \ / ? * [ ].

Sheet Select makes the sheet the current one; the actions inside it
work on that sheet. Without it the sheet that is active in the
workbook is used.");

        public static readonly HelpTopic ExcelIndex = new HelpTopic("Index (Excel)",
@"The position of the sheet, 1 for the first one. With a sheet name
it chooses among the sheets that match. A number beyond the last
one chooses the last. Empty counts as 1 (Sheet Select); for Delete
Sheet empty means the current sheet.");

        public static readonly HelpTopic ExcelPosition = new HelpTopic("Position",
@"Where the new sheet goes: at the End, at the Start, or before /
after the current sheet.");

        public static readonly HelpTopic ExcelSelectNew = new HelpTopic("Select it",
@"ON    the new sheet becomes the current one
OFF   the current sheet stays the current one");

        public static readonly HelpTopic ExcelCell = new HelpTopic("Cell",
@"Cell Get, Cell Set: one cell, for example B2 or $B$2, or a name
defined in the workbook.
Find: the area to search, for example A1:D100 (empty: the whole
sheet).
Range Get: the area to read (A1:D100). Range Set: the top-left cell
where the table is written (if an area is given, its first cell).
Excel Info (Last Row, Last Column): any cell in the column or row to
measure; B7 or B:B for a column, 7:7 for a row.

Pick: select the cell in Excel and press the Pick key.

With Expr. (cell) ON the field is an expression that gives the
address, so a loop can walk down a column:

    ""B"" + {row}        B2, B3, B4... while the counter row counts");

        public static readonly HelpTopic ExcelGet = new HelpTopic("Get (Cell Get)",
@"Value         the value, typed: a number, a date, true/false or
              text. An error cell gives its text (#N/A, #DIV/0!);
              an empty cell gives empty text.
Text          the text as Excel shows it, with the number format
              (also when the column is too narrow and Excel shows ####)
Formula       the formula, for example =A1*2 (a plain value as is)
NumberFormat  for example General, 0.00, dd/mm/yyyy, @ (text)
IsEmpty       true when the cell has nothing in it");

        public static readonly HelpTopic ExcelInfo = new HelpTopic("Info (Excel Info)",
@"LastRow        the last row that has something in the column
LastColumn     the last column that has something in the row
               (0 when the column or row is empty)
UsedRange      for example A1:D20
SheetCount     how many sheets the workbook has
SheetName      the name of the current sheet
WorkbookName   for example report.xlsx
WorkbookPath   the full path (the name when it has no file yet)
Selection      the address that is selected in Excel now");

        public static readonly HelpTopic ExcelSetAs = new HelpTopic("Write as",
@"Auto     as the value is: a number, date or true/false from an
         expression stays that type. Text is read like typed
         into the cell: 007 becomes the number 7, 1/2 a date,
         and text that starts with = is a formula.
Text     always text: 007 stays 007 (the cell is set to the
         Text format)
Formula  the value is a formula, with English function names and
         commas, as in the formula bar: SUM(A1,B1)");

        public static readonly HelpTopic ExcelValue = new HelpTopic("Value (Excel) and tables",
@"Cell Set   what to write.
Find       the text to look for.
Range Set  the table variable to write, for example {t}
           (always an expression); see Tables below.
Evaluate   an Excel formula to calculate, for example
           SUM(A1:A10)   COUNTIF(B:B,""x"")   TODAY()
           (up to 255 characters; the sheet that is current is
           the one the formula is calculated on).

With Expr. (value) ON the field is an expression, so a variable
or a calculation can be used; a number stays a number.

Sheet Rename  the new name of the sheet.
Copy          the cell where the copy goes (its top-left cell), for
              example E1; with Expr. ON an expression such as
              ""E"" + {row}.
Excel Macro     an argument for the macro (optional). Literal text is
              passed as text; with Expr. ON a number, date or
              true/false from an expression keeps its type
              ({n} * 2 passes a number). Empty: no argument.

Tables: Range Get -> {t}; {t.Rows}, {t.Columns}, {t[row, column]}
(counted from 1). Range Set writes {t} (or a copy of any table
variable) from a top-left cell. Get (Value, Text or Formula) says
what is read; Write as says how it is written.

Walk down a table with a Loop of type For:

    Range Get  A1:D100  -> t
    Loop For   row = 1 to {t.Rows}
        Cell Set  ""F"" + {row}  =  {t[row, 2]} * {t[row, 3]}

A table is a copy: changing the sheet later does not change it.
Copy a block to another sheet or workbook: Range Get, then
Sheet Select / Workbook, then Range Set.");

        public static readonly HelpTopic ExcelFind = new HelpTopic("Find (Excel)",
@"Looks for the text in the cells (their values, as shown). The first
match in reading order (row by row) is used. The actions inside run
when something is found.

Match case   upper and lower case differ
Whole cell   the whole cell must equal the text; otherwise a part
             of the cell is enough");

        public static readonly HelpTopic ExcelSave = new HelpTopic("Save",
@"Save As OFF  saves the workbook to its own file (a workbook that
              has no file yet needs Save As).
Save As ON    saves under the New file, which also becomes the
              file of the workbook. The extension chooses the
              format: .xlsx .xlsm .xlsb .xls, .csv (UTF-8, only
              the current sheet) or .pdf (a copy; the workbook
              keeps its file). An existing file is replaced
              without asking.");

        public static readonly HelpTopic ExcelSaveChanges = new HelpTopic("Save changes",
@"Workbook Close   ON saves the workbook first (it needs a file);
                 OFF drops the changes.
Excel Close      closes the Excel that holds the current workbook.
                 ON saves the workbooks that have a file; OFF drops
                 all changes. A workbook that has no file yet is
                 never saved by this (use Save As first).
                 This closes Excel with everything open in it.");

        public static readonly HelpTopic ExcelTimeout = new HelpTopic("Timeout (Excel)",
@"How long to wait for Excel: to start, to open a file, and while
Excel is busy (a cell is being edited, a dialog is open). After
that the action fails.

It does NOT limit how long Refresh and Excel Macro run: once Excel
has started them they run to the end, and the Stop key takes
effect only after they finish. A macro that shows a dialog
(MsgBox, InputBox) waits for you: use a visible Excel for it.

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic LoopForId = new HelpTopic("ID (Loop For)",
@"The variable that holds the counter. Use it in the actions inside
the loop as {id}, for example in a cell address.");

        public static readonly HelpTopic LoopFor = new HelpTopic("From, To, Step (Loop For)",
@"Counts from From to To (both included) in steps of Step, and runs
the actions inside once for each value. Each can be a number or an
expression:

From 2   To {last}   Step 1        rows 2 to the last row
From 10  To 1        Step -1       counting down

Step cannot be 0. When the numbers do not lead from From to To
(From 5, To 1, Step 1) the actions inside do not run.");

        public static readonly HelpTopic WebUrl = new HelpTopic("URL (Tab New)",
@"The address to open in a new tab. https:// is added when missing.
Empty opens the start page of the browser.

If the browser is not running it is started first (that can take
a while: see Timeout). The actions inside this action work in that
tab.");

        public static readonly HelpTopic WebTitle = new HelpTopic("Title (Tab Switch)",
@"A regular expression for the title of the tab, not case sensitive.
It may match any part of the title, so a title that changes (the
number of messages, a file name...) can still be found:

Inbox            titles that contain Inbox
^Orders \(\d+\)  Orders (3), Orders (12)...
(Report|Summary) Report or Summary anywhere in the title

Title, URL and Index are three ways to find the tab. With both a
Title and a URL, the tab has to match both. None is required: with
nothing filled in the first tab of the window you used last is used.

Tabs are counted in this order: the window you used last first,
then the other windows; in each window from left to right.
With a Title and/or URL: the Index-th tab that matches (1 = the
first; the action waits up to Timeout for a match); if there are
fewer, the last one.");

        public static readonly HelpTopic WebUrlPattern = new HelpTopic("URL (Tab Switch)",
@"A regular expression for the address of the tab, not case sensitive.
Use it when the title changes but the address does not:

mail\.example\.com       tabs of that site
/orders/\d+              an order page
^https://shop\.          any page of shop.

Remember that . means any character; write \. for a real dot.
See Title for how the tab is chosen when Title, URL and Index are
combined.");

        public static readonly HelpTopic WebIndex = new HelpTopic("Index",
@"Wait, Input, Click, Element Get:
  Which of the elements that match the Selector (in the Frame) to
  use: 1 is the first one, 2 the second... A number beyond the last
  match chooses the last match. Empty counts as 1. The Pick button
  fills it in when the element it picked is not the only match.

Tab Switch:
  The position of the tab. Without a Title or URL: the Index-th tab
  of the window you used last. With a Title and/or URL: the Index-th
  of the tabs that match. A number beyond the last one chooses the
  last. Empty counts as 1, so with nothing filled in the first tab
  of the window you used last is used.");

        public static readonly HelpTopic WebGet = new HelpTopic("Get (Element Get)",
@"What to read from the element; it goes into the variable ID.

Text          the visible text of a button, label, span, div...;
              for a field (input, textarea, select) its value
Value         the value of a field; for a select the value of the
              chosen option; for a date or time field the text in the
              format of the field (2026-11-14, 2026-11-14T10:30)
SelectedText  the text of the chosen option(s) of a select (joined
              with a comma); of a checked checkbox or radio button
              the text of its label
Checked       true or false: checkbox, radio button, option, or
              aria-checked. Use it in a Condition: {agree}
Html          the HTML inside the element
OuterHtml     the HTML of the element itself
Attribute     one attribute: href, src, data-id, class, aria-label...
Count         how many elements match the Selector (a number, 0 when
              none; does not wait). Condition: {rows} > 0
PageUrl       the address of the tab
PageTitle     the title of the tab

Radio buttons: select the chosen one with :checked, for example
input[name=size]:checked, and read Value or SelectedText.

Text and numbers are text when they come from the page: use a
Variable action (type Number, Date...) to convert them.");

        public static readonly HelpTopic WebAttribute = new HelpTopic("Attribute (Element Get)",
@"The name of the attribute to read, for example href, src, alt,
data-id, class, placeholder, aria-label. An attribute that is not
there gives empty text.");

        public static readonly HelpTopic WebScript = new HelpTopic("Script (Web)",
@"JavaScript that runs in the page, like the browser console (F12,
Console) of the current tab. Several lines, await and promises are
fine, and let/const can be used again in the next Script action.
It runs in the page itself (not in a frame).

The value of the last expression is kept in the variable ID when an
ID is given: text, true/false and numbers as they are; an object or
an array as JSON text; nothing as empty text.

    document.title
    document.querySelectorAll('tr').length
    await fetch('/api/ping').then(r => r.status)
    localStorage.getItem('token')

An error in the script stops the run and shows the message.
Timeout is how long the script may run.

With Expression ON the Script is an expression that makes the
JavaScript text, so variables can be put in: ""alert('"" + {name} + ""')""");

        public static readonly HelpTopic WebText = new HelpTopic("Text (Web)",
@"Press Key       Enter, Tab, Escape, Backspace, Delete, Space, Up,
                 Down, Left, Right, Home, End, PageUp, PageDown,
                 F1 to F12, a letter or a digit; with modifiers:
                 Ctrl+A, Shift+Tab, Alt+Left, Ctrl+Shift+Z.
                 Ctrl+A, C, X, V, Z and Y work in a text field.
                 A Selector focuses that element first.
Select Option   the text or the value of the option to choose in a
                select list (a part of the text is enough)
Upload          the file for a file input; several files: path1|path2
Screenshot      the file to save (.png or .jpg)
Wait for        the pattern of the address, title or page text
Input   the text typed into the element; what was in it is replaced.
        Vietnamese and other Unicode text is typed as is.
Dialog  the answer typed into a prompt box (when accepting).
Tab Switch  a word from the title or the address of the tab to use;
        not case sensitive. If several tabs match, the newest one.");

        public static readonly HelpTopic WebDialog = new HelpTopic("Accept (Dialog)",
@"Answers a JavaScript alert, confirm or prompt box that the page has
opened (for example right after a Click).

ON    OK
OFF   Cancel

Page boxes that look like a dialog (HTML) are normal elements: use
Click. Boxes of the browser itself (permissions, downloads) cannot
be reached from here: use Image and Mouse.");

        public static readonly HelpTopic WebTimeout = new HelpTopic("Timeout (Web)",
@"How long to wait before the action fails:
Open Browser   for the browser extension to connect
Open URL       for the page to finish loading
Wait, Input, Click, Element Get   for the element to appear
Tab Switch     for a matching tab to exist
Dialog         for a dialog to appear

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic KeyDelay = new HelpTopic("Delay (Keyboard)",
@"How long to wait after this action has finished, before the next action
starts. Use it to give the program time to react to the keys.

Can be given in milliseconds, seconds, minutes or hours.");

        public static readonly HelpTopic ValueType = new HelpTopic("Value type",
@"Position   a point (X, Y); pick it on screen or type it
Range      a rectangle (X, Y, Width, Height); pick it on screen
String     text
Number     a number, 3.5 or 3,5 both accepted (stored with a dot)
Boolean    true or false
Date       a date (no time)
DateTime   a date and a time

With 'Use an expression' the result is converted to this type. Declaring the
same ID again with another type replaces the variable and the later type wins.");

        public static readonly HelpTopic PositionPick = new HelpTopic("Position and Range",
@"A Position or Range variable holds only numbers. Which window and anchor they
are measured from is decided by the action that uses the variable, for example
a Mouse action's Process and Anchor.

The Pick button fills the numbers from the screen (desktop coordinates).");
    }
}
