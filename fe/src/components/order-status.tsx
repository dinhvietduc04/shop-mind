import type { OrderStatus } from "@/lib/api/types";
import { BAND_BG } from "@/lib/bands";
import { cn } from "@/lib/cn";
import { MAINLINE, ORDER_BAND, mainlineRank } from "@/lib/orders/lifecycle";

/** The fulfillment path is the lifecycle's mainline — no second ordering
 *  that must "agree by luck" with the transition map (C5). */
const ORDER_STEPS = MAINLINE;

/** Band-coloured status chip — the digits of order state, always printed. */
export function StatusChip({ status }: { status: OrderStatus }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-2 rounded-[3px] border-[1.5px] border-[#1a1a1a] px-2 py-0.5 font-mono text-[11px] font-bold uppercase tracking-[0.16em]",
        BAND_BG[ORDER_BAND[status]],
        ["yellow", "grey", "white"].includes(ORDER_BAND[status])
          ? "text-[#1a1a1a]"
          : "text-[#fffdf7]"
      )}
    >
      {status}
    </span>
  );
}

/** Five-dot decode of the fulfillment path. Terminal side-branches mark the
 *  stop point with their own colour and never fake forward progress. */
export function StatusTimeline({ status }: { status: OrderStatus }) {
  const branch = mainlineRank(status) === -1; // terminal side branch (ADR-0005)
  const reached = branch ? 0 : ORDER_STEPS.indexOf(status) + 1;
  return (
    <div>
      <ol className="flex items-center gap-0" aria-label={`Order status: ${status}`}>
        {ORDER_STEPS.map((step, i) => {
          const done = i < reached;
          return (
            <li key={step} className="flex items-center">
              <span
                title={step}
                className={cn(
                  "h-3.5 w-3.5 rounded-full border-[1.5px] border-[#1a1a1a]",
                  done
                    ? BAND_BG[ORDER_BAND[status]]
                    : "bg-transparent border-ink/30"
                )}
              />
              {i < ORDER_STEPS.length - 1 && (
                <span
                  aria-hidden
                  className={cn(
                    "h-[1.5px] w-6",
                    i < reached - 1 ? "bg-ink" : "bg-ink/20"
                  )}
                />
              )}
            </li>
          );
        })}
      </ol>
      <p className="mt-2 font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">
        {branch
          ? `stopped — ${status.toLowerCase()}`
          : ORDER_STEPS[reached - 1]}
      </p>
    </div>
  );
}
