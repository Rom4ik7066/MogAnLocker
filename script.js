(() => {
            const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
            const themeSelect = document.querySelector(".theme-select");
            const systemTheme = window.matchMedia("(prefers-color-scheme: light)");
            const themeStorageKey = "moglocker-theme";
            let savedTheme = "system";

            try {
                savedTheme = window.localStorage.getItem(themeStorageKey) || "system";
            } catch (error) {
                savedTheme = "system";
            }

            if (!["system", "light", "dark"].includes(savedTheme)) {
                savedTheme = "system";
            }

            const applyTheme = (preference) => {
                const resolvedTheme = preference === "system"
                    ? (systemTheme.matches ? "light" : "dark")
                    : preference;
                document.documentElement.dataset.theme = resolvedTheme;
                const themeColor = document.querySelector('meta[name="theme-color"]');
                if (themeColor) {
                    themeColor.content = resolvedTheme === "light" ? "#f4f6fa" : "#0b0d12";
                }
                if (themeSelect) {
                    themeSelect.value = preference;
                }
            };

            applyTheme(savedTheme);

            if (themeSelect) {
                themeSelect.addEventListener("change", () => {
                    savedTheme = themeSelect.value;
                    applyTheme(savedTheme);
                    try {
                        window.localStorage.setItem(themeStorageKey, savedTheme);
                    } catch (error) {
                        savedTheme = themeSelect.value;
                    }
                    drawParticles();
                });
            }

            const onSystemThemeChange = () => {
                if (savedTheme === "system") {
                    applyTheme("system");
                    drawParticles();
                }
            };

            if (systemTheme.addEventListener) {
                systemTheme.addEventListener("change", onSystemThemeChange);
            } else if (systemTheme.addListener) {
                systemTheme.addListener(onSystemThemeChange);
            }

            const canvas = document.querySelector("#ambient-canvas");
            const context = canvas && canvas.getContext("2d");
            let particles = [];
            let particleFrame = 0;

            const resizeCanvas = () => {
                if (!canvas || !context) {
                    return;
                }

                const ratio = Math.min(window.devicePixelRatio || 1, 1.5);
                const width = window.innerWidth;
                const height = window.innerHeight;
                canvas.width = Math.round(width * ratio);
                canvas.height = Math.round(height * ratio);
                context.setTransform(ratio, 0, 0, ratio, 0, 0);

                const count = Math.max(24, Math.min(72, Math.round((width * height) / 18000)));
                particles = Array.from({ length: count }, () => ({
                    x: Math.random() * width,
                    y: Math.random() * height,
                    dx: (Math.random() - .5) * .24,
                    dy: (Math.random() - .5) * .24,
                    radius: Math.random() * 1.5 + .7
                }));
                drawParticles();
            };

            function drawParticles() {
                if (!canvas || !context) {
                    return;
                }

                window.cancelAnimationFrame(particleFrame);
                const width = window.innerWidth;
                const height = window.innerHeight;
                const particleColor = getComputedStyle(document.documentElement)
                    .getPropertyValue("--particle").trim() || "181, 243, 107";
                const linkColor = getComputedStyle(document.documentElement)
                    .getPropertyValue("--particle-link").trim() || "169, 155, 255";

                context.clearRect(0, 0, width, height);
                particles.forEach((particle, index) => {
                    if (!reduceMotion && !document.hidden) {
                        particle.x += particle.dx;
                        particle.y += particle.dy;
                        if (particle.x < 0 || particle.x > width) particle.dx *= -1;
                        if (particle.y < 0 || particle.y > height) particle.dy *= -1;
                    }

                    context.beginPath();
                    context.arc(particle.x, particle.y, particle.radius, 0, Math.PI * 2);
                    context.fillStyle = `rgba(${particleColor}, .42)`;
                    context.fill();

                    for (let otherIndex = index + 1; otherIndex < particles.length; otherIndex += 1) {
                        const other = particles[otherIndex];
                        const distance = Math.hypot(particle.x - other.x, particle.y - other.y);
                        if (distance < 118) {
                            context.beginPath();
                            context.moveTo(particle.x, particle.y);
                            context.lineTo(other.x, other.y);
                            context.strokeStyle = `rgba(${linkColor}, ${(1 - distance / 118) * .16})`;
                            context.lineWidth = 1;
                            context.stroke();
                        }
                    }
                });

                if (!reduceMotion && !document.hidden) {
                    particleFrame = window.requestAnimationFrame(drawParticles);
                }
            }

            if (canvas && context) {
                resizeCanvas();
                window.addEventListener("resize", resizeCanvas, { passive: true });
                document.addEventListener("visibilitychange", drawParticles);
            }

            const progress = document.querySelector(".scroll-progress");
            const revealItems = document.querySelectorAll(
                ".hero > div:first-child, .section-label, .section-heading, .card, .about, .cta > *, .details-intro, .detail-card, .step-card, .notice"
            );
            const navLinks = document.querySelectorAll('.nav a[href^="#"]');

            if (!reduceMotion && "IntersectionObserver" in window) {
                document.documentElement.classList.add("js-motion");

                const revealObserver = new IntersectionObserver((entries, observer) => {
                    entries.forEach((entry) => {
                        if (entry.isIntersecting) {
                            entry.target.classList.add("is-visible");
                            observer.unobserve(entry.target);
                        }
                    });
                }, { threshold: 0.14 });

                revealItems.forEach((item) => {
                    item.classList.add("reveal");
                    revealObserver.observe(item);
                });
            }

            if (!reduceMotion && window.matchMedia("(pointer: fine)").matches) {
                document.querySelectorAll(".card").forEach((card) => {
                    card.addEventListener("pointermove", (event) => {
                        const bounds = card.getBoundingClientRect();
                        card.style.setProperty("--pointer-x", `${event.clientX - bounds.left}px`);
                        card.style.setProperty("--pointer-y", `${event.clientY - bounds.top}px`);
                    });
                });
            }

            let progressQueued = false;
            const updateProgress = () => {
                if (!progressQueued) {
                    progressQueued = true;
                    window.requestAnimationFrame(() => {
                        const scrollableHeight = document.documentElement.scrollHeight - window.innerHeight;
                        const amount = scrollableHeight > 0 ? window.scrollY / scrollableHeight : 0;
                        if (progress) {
                            progress.style.transform = `scaleX(${Math.min(1, Math.max(0, amount))})`;
                        }
                        progressQueued = false;
                    });
                }
            };

            window.addEventListener("scroll", updateProgress, { passive: true });
            window.addEventListener("resize", updateProgress);
            updateProgress();

            if ("IntersectionObserver" in window && navLinks.length) {
                const sections = Array.from(navLinks)
                    .map((link) => document.querySelector(link.getAttribute("href")))
                    .filter(Boolean);
                const sectionObserver = new IntersectionObserver((entries) => {
                    entries.forEach((entry) => {
                        if (entry.isIntersecting) {
                            navLinks.forEach((link) => {
                                if (link.getAttribute("href") === `#${entry.target.id}`) {
                                    link.setAttribute("aria-current", "location");
                                } else {
                                    link.removeAttribute("aria-current");
                                }
                            });
                        }
                    });
                }, { rootMargin: "-25% 0px -60% 0px" });

                sections.forEach((section) => sectionObserver.observe(section));
            }
        })();
