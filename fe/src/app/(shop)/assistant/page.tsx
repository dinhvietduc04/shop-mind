import { AssistantPanel } from "@/components/assistant-panel";

export default function AssistantPage() {
  return (
    <div className="mx-auto max-w-[760px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
        The assistant
      </h1>
      <p className="mt-2 max-w-[62ch] text-sm leading-relaxed text-ink-soft">
        F0 ships the drawer demo below. The full chat surface — streaming replies,
        attribute-elicitation cards, and per-turn tool trails — lands at
        FRONTEND_PLAN phase F4, driven through the same contract.
      </p>
      <div className="mt-6">
        <AssistantPanel />
      </div>
    </div>
  );
}
