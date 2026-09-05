/**
 * When something happened, in the reader's own locale and time zone.
 *
 * Two spellings, and screens use both: the exact moment where somebody may have
 * to compare it with something else, and how long ago it was where the
 * interesting part is "recently" or "months back". A row that shows the short
 * one carries the long one as its title, so nothing is only ever approximate.
 *
 * The instance answers in UTC with an offset (`docs/api.md`); nothing here
 * parses or arithmetics on strings.
 */
const exact = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

export function when(moment: string | null | undefined): string {
  return moment == null ? "" : exact.format(new Date(moment));
}

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

const spans = [
  { unit: "year", seconds: 31_536_000 },
  { unit: "month", seconds: 2_592_000 },
  { unit: "day", seconds: 86_400 },
  { unit: "hour", seconds: 3_600 },
  { unit: "minute", seconds: 60 },
] as const;

/** "3 days ago", "in 2 hours", "just now" — the sentence a row can end in. */
export function around(moment: string | null | undefined, now = Date.now()): string {
  if (moment == null) {
    return "";
  }

  const seconds = (new Date(moment).getTime() - now) / 1000;

  for (const span of spans) {
    if (Math.abs(seconds) >= span.seconds) {
      return relative.format(Math.round(seconds / span.seconds), span.unit);
    }
  }

  return "just now";
}
