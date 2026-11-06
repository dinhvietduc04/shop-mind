import { Masthead } from "@/components/shell";

export default function DevLayout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <Masthead />
      {children}
    </>
  );
}
