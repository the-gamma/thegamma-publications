install.packages("ggplot2")
library(ggplot2)
library(reshape2)

dr <- read.csv("C:/Tomas/Research/TheGamma/wip-live-programming/experiments/drawing.csv")
names(dr) <- c("call-by-value", "lazy", "live")
dr$time <- seq.int(nrow(dr))

drm <- melt(dr,id = c("time"))
names(drm) <- c("time", "method", "delay")

a1 <- arrow(type = "open", angle = 90, length = unit(0.15, "inches"))
a2 <- arrow(type = "open", angle = 90, length = unit(0.3, "inches"))

ggplot(drm,aes(x = time,y = delay)) + 
  geom_bar(aes(fill = method),stat = "identity",position = "dodge") +
  scale_fill_manual("Evaluation method", values = c("lazy" = "#aec7e8", "call-by-value" = "#1f77b4", "live" = "#ff7f0e")) +
  xlab("Token (number)") + ylab("Delay (ms)") +
  annotate("text", x = 37.5, y = 3250, label = c("(h) let")) +
  geom_segment(aes(x = 37.5, y = 3100, xend = 37.5, yend = 2900), size=1, arrow = a2) +
  annotate("text", x = 32.5, y = 3250, label = c("(g) combine")) +
  geom_segment(aes(x = 32.5, y = 3100, xend = 32.5, yend = 2900), size=1, arrow = a2) +
  annotate("text", x = 22, y = 2550, label = c("(f) combine")) +
  geom_segment(aes(x = 22, y = 2400, xend = 22, yend = 1950), size=1, arrow = a1) +
  annotate("text", x = 20, y = 2350, label = c("(e) let")) +
  geom_segment(aes(x = 20, y = 2200, xend = 20, yend = 2050), size=1, arrow = a1) +
  annotate("text", x = 15.5, y = 2400, label = c("(d) blur")) +
  geom_segment(aes(x = 15.5, y = 2250, xend = 15.5, yend = 2000), size=1, arrow = a2) +
  annotate("text", x = 12, y = 1150, label = c("(c) blur")) +
  geom_segment(aes(x = 12, y = 1000, xend = 12, yend = 750), size=1, arrow = a1) +
  annotate("text", x = 10, y = 1350, label = c("(b) grey")) +
  geom_segment(aes(x = 10, y = 1200, xend = 10, yend = 900), size=1, arrow = a1) +
  annotate("text", x = 6, y = 1250, label = c("(a) load")) +
  geom_segment(aes(x = 6, y = 1100, xend = 6, yend = 850), size=1, arrow = a1) 



drf <- dr[dr$`call-by-value`>15|dr$lazy>15|dr$live>15, ] 
drfm <- melt(drf,id = c("time"))
names(drfm) <- c("time", "method", "delay")

ggplot(drm, aes(x=delay, fill=method, color=method)) + 
  geom_histogram(binwidth=100,position="dodge") +
  facet_grid(method ~ .) +
  scale_fill_manual("Evaluation method", values=c("#1f77b4", "#aec7e8", "#ff7f0e")) +
  scale_color_manual("Evaluation method", values=c("#1f77b4", "#aec7e8", "#ff7f0e")) +
  ylab("Count") + xlab("Delay (ms)")


ggplot(drfm, aes(x=delay, fill=method, color=method)) + 
  geom_histogram(binwidth=100,position="dodge") +
  facet_grid(method ~ .) +
  scale_fill_manual("Evaluation method", values=c("#1f77b4", "#aec7e8", "#ff7f0e")) +
  scale_color_manual("Evaluation method", values=c("#1f77b4", "#aec7e8", "#ff7f0e")) +
  ylab("Count") + xlab("Delay (ms)")

