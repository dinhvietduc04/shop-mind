"use client";

import { ChatPanel } from "@/components/chat";

/**
 * Catalog right-column drawer and mobile bottom slot. It shares one store and
 * one conversation with /assistant. Quote cards link to the bill; no pay
 * control renders here.
 */
export function AssistantPanel() {
  return <ChatPanel compact />;
}
