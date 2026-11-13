"use client";

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { LogIn } from "lucide-react";
import { z } from "zod";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { ApiError } from "@/infrastructure/api";
import { useSession } from "@/lib/session";

const loginSchema = z.object({
  email: z.email("Enter a valid email address."),
  password: z.string().min(1, "Password is required."),
});
type LoginValues = z.infer<typeof loginSchema>;

const DEMO_IDENTITIES = [
  { label: "Demo customer", email: "customer@shopmind.local", password: "Customer123!" },
  { label: "Demo admin", email: "admin@shopmind.local", password: "Admin123!" },
];

/** Only same-origin, non-proto relative paths are safe to bounce to. */
export function safeNext(raw: string | null | undefined, fallback = "/") {
  return raw && raw.startsWith("/") && !raw.startsWith("//") ? raw : fallback;
}

function FieldError({ children }: { children: React.ReactNode }) {
  return (
    <span role="alert" className="mt-1 block font-mono text-[11px] text-band-red">
      {children}
    </span>
  );
}

function AuthCard({ title, kicker, children }: { title: string; kicker: string; children: React.ReactNode }) {
  return (
    <main className="mx-auto flex min-h-[calc(100vh-57px)] max-w-[460px] flex-col justify-center px-6 py-10">
      <p className="font-mono text-[11px] uppercase tracking-[0.22em] text-ink-soft">{kicker}</p>
      <h1 className="mt-2 text-2xl font-extrabold uppercase tracking-[0.04em]">{title}</h1>
      <div className="mt-6 rounded-label border-[1.5px] border-ink bg-card p-5">{children}</div>
    </main>
  );
}

/** Shared field styling so /login and /register read as one system. */
export const authInput =
  "mt-1 w-full rounded-[4px] border-[1.5px] border-line bg-paper px-3 py-2 font-mono text-[13px] focus:border-ink";
export const authLabel = "block font-mono text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft";

function LoginForm({ next, onDone }: { next: string; onDone: () => void }) {
  const login = useSession((s) => s.login);
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginValues>({ mode: "onTouched" });

  const submit = handleSubmit(async (values) => {
    setServerError(null);
    const parsed = loginSchema.safeParse(values);
    if (!parsed.success) {
      for (const issue of parsed.error.issues) {
        setError(issue.path[0] as keyof LoginValues, { message: issue.message });
      }
      return;
    }
    try {
      await login(parsed.data.email, parsed.data.password);
      onDone();
    } catch (e) {
      if (e instanceof ApiError) {
        for (const [field, msgs] of Object.entries(e.errors ?? {})) {
          if (field) setError(field.toLowerCase() as keyof LoginValues, { message: msgs[0] });
        }
        setServerError(e.title);
      } else setServerError("Could not reach the shop. Try again.");
    }
  });

  const demo = (d: (typeof DEMO_IDENTITIES)[number]) => async () => {
    setServerError(null);
    try {
      await login(d.email, d.password);
      onDone();
    } catch (e) {
      setServerError(e instanceof ApiError ? e.message : "Sign-in failed.");
    }
  };

  return (
    <>
      <form onSubmit={submit} noValidate className="space-y-4">
        <label>
          <span className={authLabel}>Email</span>
          <input type="email" autoComplete="email" {...register("email")} className={authInput} placeholder="you@example.com" />
          {errors.email && <FieldError>{errors.email.message}</FieldError>}
        </label>
        <label>
          <span className={authLabel}>Password</span>
          <input type="password" autoComplete="current-password" {...register("password")} className={authInput} placeholder="••••••••" />
          {errors.password && <FieldError>{errors.password.message}</FieldError>}
        </label>

        {serverError && (
          <ErrorNote>
            <strong>{serverError}</strong> The mock shop holds two seeded accounts. Click either account below to sign in.
          </ErrorNote>
        )}

        <Button type="submit" variant="human" className="w-full justify-center" disabled={isSubmitting}>
          <LogIn size={13} aria-hidden />
          {isSubmitting ? "Checking…" : "Sign in"}
        </Button>
      </form>

      <div className="mt-5 border-t-[1.5px] border-dashed border-line pt-4">
        <p className="mb-2 font-mono text-[11px] uppercase tracking-[0.18em] text-ink-soft">one-click demo identities</p>
        <div className="grid grid-cols-2 gap-2">
          {DEMO_IDENTITIES.map((d) => (
            <Button key={d.email} variant="ghost" disabled={isSubmitting} onClick={demo(d)}>
              {d.label}
            </Button>
          ))}
        </div>
      </div>

      <p className="mt-5 text-center font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
        no account?{" "}
        <Link href="/register" className="text-ink underline underline-offset-4">
          create one
        </Link>
      </p>
      <p className="mt-3 text-center font-mono text-[11px] uppercase tracking-[0.14em]">
        <Link href={next} className="text-ink-soft underline decoration-line underline-offset-4 hover:text-ink">
          continue as guest and browse the bins
        </Link>
      </p>
    </>
  );
}

function LoginInner() {
  const router = useRouter();
  const params = useSearchParams();
  const next = safeNext(params?.get("next"));
  const user = useSession((s) => s.user);

  /* Redirect from the parent, not the form's callback. The form unmounts the
     moment `user` lands, and an unmounted router.push can be dropped. */
  useEffect(() => {
    if (user) router.replace(next);
  }, [user, next, router]);

  if (user) {
    return (
      <AuthCard kicker="session" title={`Welcome back, ${user.firstName}`}>
        <p className="text-[14px] leading-relaxed">
          Signed in as <strong>{user.email}</strong> ({user.role}). Returning to{" "}
          <span className="font-mono text-[12px]">{next}</span>.{" "}
          <Link href={next} className="underline underline-offset-4">
            Continue
          </Link>
          .
        </p>
      </AuthCard>
    );
  }

  return (
    <AuthCard kicker="shopmind · session" title="Sign in">
      <LoginForm next={next} onDone={() => { /* parent effect redirects */ }} />
    </AuthCard>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<Skeleton className="mx-auto mt-6 h-64 max-w-[460px]" />}>
      <LoginInner />
    </Suspense>
  );
}
