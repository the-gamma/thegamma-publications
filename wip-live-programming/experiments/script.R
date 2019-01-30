install.packages("ggplot2")
library(ggplot2)
library(reshape2)

dr <- read.csv("C:/Tomas/Research/TheGamma/wip-live-programming/experiments/drawing.csv")
names(dr) <- c("full", "live")
dr$time <- seq.int(nrow(dr))

drm <- melt(dr,id = c("time"))
names(drm) <- c("time", "method", "delay")

ggplot(drm,aes(x = time,y = delay)) + 
  geom_bar(aes(fill = method),stat = "identity",position = "dodge")
