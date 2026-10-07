import { useEffect } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";

/**
 * Rendered for any path outside the route table and for a board whose
 * handle no owner holds. The server answers both with a 404 status (see
 * SpaFallback.cs), so this page only has to look right; crawlers already
 * know the URL is dead.
 */
export function NotFoundPage() {
  // Most pages never set a title, so put back the one this page replaced.
  useEffect(() => {
    const previous = document.title;
    document.title = "Page not found";
    return () => {
      document.title = previous;
    };
  }, []);

  return (
    <div className="flex min-h-screen items-center justify-center p-8">
      <div className="max-w-md space-y-4 text-center">
        <i
          className="fa-sharp fa-regular fa-compass text-muted-foreground text-4xl"
          aria-hidden="true"
        />
        <h1 className="text-2xl font-semibold">Page not found</h1>
        <p className="text-muted-foreground">
          Check the address for typos. The page may also have moved or been removed.
        </p>
        <Button asChild>
          <Link to="/">Go to home page</Link>
        </Button>
      </div>
    </div>
  );
}
