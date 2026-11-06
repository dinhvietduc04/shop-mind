import { Masthead } from "@/components/shell";

// The bin rail is NOT here on purpose: filtering bins only means something on
// the catalog itself. Bill/assistant/order surfaces get a focused full-width
// layout (docs/FRONTEND_PLAN.md F3).
export default function ShopLayout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <Masthead />
      <div className="mx-auto min-h-[calc(100vh-57px)] max-w-[1600px]">{children}</div>
    </>
  );
}
