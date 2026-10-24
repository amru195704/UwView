# Measuring the GUI by hand

Some things only exist on screen: the time until a big file is open, jumping to the last line,
how the window behaves with one huge line. They are measured by hand and written to a CSV with the
same columns as `summary.csv` (copy [`../tools/gui-template.csv`](../tools/gui-template.csv)).

1. Use the files made by `tools/gen.py` (same bytes as the command-line runs).
2. For cold, drop the cache first (see [00-rules.md](00-rules.md)); for hot, open the file once before.
3. Start the stopwatch when you press Open (or Enter in the search box), stop it when the line count
   or the hit count is final.
4. Three runs, take the median. Write `product` as `UwView`, `UwView Pro` or `Web`.

| action | start | stop |
|---|---|---|
| open | Open is pressed | the line count in the status bar no longer changes |
| tail | Ctrl/Cmd+End is pressed | the last line is shown |
| search | Enter in the search box | the hit count is final |
