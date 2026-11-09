"use client";

import { ChatPanel } from "@/components/chat";

/**
 * Catalog right-column drawer (and mobile bottom slot). F4 made it live:
 * the same ChatPanel as /assistant, sharing one store, one conversation.
 * The Assist invariant is structural, not decorative — ChatPanel renders
 * quote cards that only LINK to the bill; no pay control exists here.
 */
export function AssistantPanel() {
  return <ChatPanel compact />;
}
