import { Masthead } from "@/components/shell";

// The bin rail is not here on purpose. Filtering bins only means something on
// the catalog itself. Bill, assistant, and order pages get a focused full-width
// layout (docs/FRONTEND_PLAN.md).
export default function ShopLayout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <a
        href="#shop-content"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-3 focus:z-50 focus:rounded-[4px] focus:border-[1.5px] focus:border-ink focus:bg-card focus:px-4 focus:py-2 focus:font-mono focus:text-[11px] focus:uppercase focus:tracking-[0.14em]"
      >
        Skip to content
      </a>
      <Masthead />
      <div id="shop-content" tabIndex={-1} className="mx-auto min-h-[calc(100vh-57px)] max-w-[1600px] focus:outline-none">
        {children}
      </div>
    </>
  );
}
