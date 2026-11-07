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

  0        = forever: repeat until you stop the script (F6 or Esc)
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

        public static readonly HelpTopic WaitTime = new HelpTopic("Wait",
@"How long one attempt waits for the window to appear and come to the front.
If it does not, the attempt is repeated (see Retry). When all attempts fail the
script stops with an error. When the window is ready the actions inside run.");

        public static readonly HelpTopic Retry = new HelpTopic("Retry",
@"How many more attempts are made after the first one times out.
0 means a single attempt. Total waiting time is (Retry + 1) x Wait.");

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
