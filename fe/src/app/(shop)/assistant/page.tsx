"use client";

import { Suspense, useEffect, useRef } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ChatPanel } from "@/components/chat";
import { BandChip } from "@/components/ui";
import { useChat } from "@/lib/chat/store";
import { bandForCategory, bandClass } from "@/lib/bands";
import { cn } from "@/lib/cn";

function AssistantInner() {
  const params = useSearchParams();
  const q = params.get("q");
  const cat = params.get("cat");
  const sent = useRef(false);

  // hand-off from a product page: seed the conversation with the unit in question
  useEffect(() => {
    if (q && !sent.current && useChat.getState().messages.length === 0) {
      sent.current = true;
      void useChat.getState().send(`help me with ${q}`);
    }
  }, [q]);

  const band = bandForCategory(cat ?? undefined);

  return (
    <>
      {q && (
        <p className="mb-4 flex items-center gap-2 rounded-[4px] border-[1.5px] border-line bg-card px-3 py-2 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
          <span aria-hidden className={cn("h-3.5 w-2.5 rounded-[2px] border-[1.5px] border-[#1a1a1a]", bandClass(band))} />
          discussing “{q}”{cat ? ` · bin ${cat}` : ""} — sent as your opening ask
        </p>
      )}
      <ChatPanel />
    </>
  );
}

export default function AssistantPage() {
  return (
    <main className="mx-auto max-w-[860px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
        The assistant
      </h1>
      <p className="mt-2 max-w-[68ch] text-sm leading-relaxed text-ink-soft">
        Assist level, live against the mock backend: it searches the bins,{" "}
        <strong>asks attribute questions before it filters</strong>, adds to your
        cart and opens quotes — and stops at the money. The engine is the
        deterministic FakeAiProvider (backend plan §A4), so every number you see
        traces to a tool chip in the same turn. The drawer on the catalog page is
        the same conversation.
      </p>
      <p className="mt-2 flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
        <BandChip band="yellow" size="sm" title="human-only" /> yellow stays yours:{" "}
        <Link href="/quote" className="underline underline-offset-4 text-ink">
          the bill page
        </Link>{" "}
        is where payment happens.
      </p>

      <div className="mt-6">
        <Suspense fallback={<div className="h-[70vh] animate-pulse rounded bg-kraft" />}>
          <AssistantInner />
        </Suspense>
      </div>
    </main>
  );
}
