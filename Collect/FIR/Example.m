clear;
clc;close all;
Num=4096;
t=linspace(0,1,Num);
Fs=1/(t(2)-t(1));
f1=100;
f2=800;
f3=1500;
S1=sin(2*pi*f1.*t);
S2=sin(2*pi*f2.*t);
S3=sin(2*pi*f3.*t);
S=S1+S2+S3;
%% 滤波器参数设置
Order=1201;
% 低通滤波
LowCof=CoefficientsGenerator(Fs,Order, 'lowpass',200);
Low_S=FIR(S,LowCof,Order,2);
%% Plot
figure(1)
plot(t,Low_S);
figure(2)
plot(t,S);
