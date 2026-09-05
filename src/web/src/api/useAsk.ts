import { useCallback, useEffect, useState } from "react";
import { describe, type Answer, type Problem } from "./client";

/**
 * What one question to the instance is in, at any moment.
 *
 * The three states are the ones `docs/human-interface.md` asks every screen to
 * design rather than leave blank: waiting, answered — which includes the answer
 * with nothing in it, and the screen's own sentence for that — and refused,
 * which carries the instance's wording rather than one of ours.
 */
export type Asked<T> =
  | { at: "asking" }
  | { at: "answered"; data: T }
  | { at: "refused"; why: string; code?: string; status: number };

/**
 * Ask the instance something, and say what happened.
 *
 * Every screen under the shell reads this way: the frame is already on the
 * screen, so what is waiting is a region inside it rather than the page. An
 * answer that arrives after the reader has left is dropped — the request is not
 * cancelled, because the instance has already done the work and a torn-down
 * screen has nowhere to put it.
 *
 * The second element is how a screen asks again after it changed something. A
 * write is followed by a re-read rather than by patching a list in place: what
 * the instance holds is what the screen shows, and a client that keeps its own
 * copy of a catalogue is a client that can disagree with it.
 *
 * **The question is named rather than watched.** `ask` is written inline at the
 * call site and is a new function on every render; what decides whether this is
 * still the same question is `asking` — `"users"`, or
 * `"secrets:landing-page/prod"`. It is also what keeps the last question's
 * answer from showing under the next one: an answer belongs to the question it
 * was asked for, and that is read here rather than reset in an effect.
 */
export function useAsk<T>(asking: string, ask: () => Promise<Answer<T>>): [Asked<T>, () => void] {
  const [answer, setAnswer] = useState<{ to: string; asked: Asked<T> }>();
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let current = true;

    void (async () => {
      try {
        const { data, error, response } = await ask();

        if (!current) {
          return;
        }

        if (data === undefined) {
          const problem = error as Problem | undefined;

          setAnswer({
            to: asking,
            asked: {
              at: "refused",
              why: describe(problem, response.status),
              code: problem?.code,
              status: response.status,
            },
          });

          return;
        }

        setAnswer({ to: asking, asked: { at: "answered", data } });
      } catch {
        if (current) {
          setAnswer({
            to: asking,
            asked: { at: "refused", why: "The instance did not answer.", status: 0 },
          });
        }
      }
    })();

    return () => {
      current = false;
    };
    // `ask` is deliberately not among these: it is a closure written at the call
    // site and would make every render a new request. What this re-asks on is
    // the name of the question, and the count that says to ask it again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [asking, attempt]);

  const again = useCallback(() => setAttempt((count) => count + 1), []);

  return [answer?.to === asking ? answer.asked : { at: "asking" }, again];
}
